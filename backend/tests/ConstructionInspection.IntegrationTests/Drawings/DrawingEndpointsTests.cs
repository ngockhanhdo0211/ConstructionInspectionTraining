using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using ConstructionInspection.Domain.Drawings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructionInspection.IntegrationTests.Drawings;

public sealed class DrawingEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UploadListAndDetail_EnforceRolesAndAllowDuplicate()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/drawings")).StatusCode);

        using var manager = await ClientAsync("manager@local.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(manager)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/drawings")).StatusCode);

        foreach (var email in new[] { "inspector@local.test", "reviewer@local.test" })
        {
            using var client = await ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/drawings")).StatusCode);
        }

        using var admin = await ClientAsync("administrator@local.test");
        var first = await UploadAsync(admin);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(firstJson.GetProperty("duplicateWarning").GetBoolean());
        var id = firstJson.GetProperty("drawing").GetProperty("id").GetGuid();

        var duplicate = await UploadAsync(admin);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.True((await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicateWarning").GetBoolean());

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/drawings?pageNumber=1&pageSize=1&sortBy=createdat&sortDirection=desc");
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
        Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/drawings/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/drawings/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Content_EnforcesAuthorizationStateRangesValidatorsAndStorageConsistency()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/drawings/{Guid.NewGuid()}/content")).StatusCode);

        foreach (var email in new[] { "inspector@local.test", "reviewer@local.test" })
        {
            using var denied = await ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await denied.GetAsync($"/api/drawings/{Guid.NewGuid()}/content")).StatusCode);
        }

        using var admin = await ClientAsync("administrator@local.test");
        var upload = await UploadAsync(admin);
        upload.EnsureSuccessStatusCode();
        var payload = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var id = payload.GetProperty("drawing").GetProperty("id").GetGuid();

        var full = await admin.GetAsync($"/api/drawings/{id}/content");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal("application/pdf", full.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", full.Headers.AcceptRanges.Single());
        Assert.True(full.Headers.CacheControl?.Private);
        Assert.True(full.Headers.CacheControl?.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, full.Headers.CacheControl?.MaxAge);
        Assert.NotNull(full.Headers.ETag);
        Assert.Equal("inline", full.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("%PDF-integration"u8.ToArray(), await full.Content.ReadAsByteArrayAsync());

        var partial = await RangeAsync(admin, id, "bytes=0-9");
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(10, partial.Content.Headers.ContentLength);
        Assert.Equal("bytes 0-9/16", partial.Content.Headers.ContentRange?.ToString());
        Assert.Equal("%PDF-integ"u8.ToArray(), await partial.Content.ReadAsByteArrayAsync());
        var storage = factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>();
        Assert.Contains((0L, 10L), storage.Ranges);

        var openEnded = await RangeAsync(admin, id, "bytes=10-");
        Assert.Equal(HttpStatusCode.PartialContent, openEnded.StatusCode);
        Assert.Equal(6, openEnded.Content.Headers.ContentLength);
        Assert.Equal("ration"u8.ToArray(), await openEnded.Content.ReadAsByteArrayAsync());

        var suffix = await RangeAsync(admin, id, "bytes=-10");
        Assert.Equal(HttpStatusCode.PartialContent, suffix.StatusCode);
        Assert.Equal(10, suffix.Content.Headers.ContentLength);
        Assert.Equal("ntegration"u8.ToArray(), await suffix.Content.ReadAsByteArrayAsync());

        var oversizedSuffix = await RangeAsync(admin, id, "bytes=-99");
        Assert.Equal(HttpStatusCode.PartialContent, oversizedSuffix.StatusCode);
        Assert.Equal("bytes 0-15/16", oversizedSuffix.Content.Headers.ContentRange?.ToString());
        Assert.Equal(16, oversizedSuffix.Content.Headers.ContentLength);

        var oneByte = await RangeAsync(admin, id, "bytes=0-0");
        Assert.Equal(HttpStatusCode.PartialContent, oneByte.StatusCode);
        Assert.Equal(new byte[] { (byte)'%' }, await oneByte.Content.ReadAsByteArrayAsync());

        using var notModifiedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/drawings/{id}/content");
        notModifiedRequest.Headers.IfNoneMatch.Add(full.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await admin.SendAsync(notModifiedRequest)).StatusCode);

        using var weakNotModifiedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/drawings/{id}/content");
        weakNotModifiedRequest.Headers.TryAddWithoutValidation("If-None-Match", $"W/{full.Headers.ETag}");
        Assert.Equal(HttpStatusCode.NotModified, (await admin.SendAsync(weakNotModifiedRequest)).StatusCode);

        using var matchingIfRangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/drawings/{id}/content");
        matchingIfRangeRequest.Headers.Range = new(0, 3);
        matchingIfRangeRequest.Headers.TryAddWithoutValidation("If-Range", full.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.PartialContent, (await admin.SendAsync(matchingIfRangeRequest)).StatusCode);

        using var ifRangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/drawings/{id}/content");
        ifRangeRequest.Headers.Range = new(0, 3);
        ifRangeRequest.Headers.TryAddWithoutValidation("If-Range", "\"different\"");
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(ifRangeRequest)).StatusCode);

        foreach (var invalidValue in new[]
                 {
                     "bytes=10-5", "bytes=999999-", "bytes=-0", "bytes=0-1,4-5", "items=0-1", "bytes=abc"
                 })
        {
            var invalid = await RangeAsync(admin, id, invalidValue);
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalid.StatusCode);
            Assert.Equal("bytes */16", invalid.Content.Headers.ContentRange?.ToString());
        }

        using var manager = await ClientAsync("manager@local.test");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/drawings/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.PartialContent, (await RangeAsync(manager, id, "bytes=0-0")).StatusCode);

        storage.BlockCopies = true;
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            var cancellationFailure = await Record.ExceptionAsync(() =>
                admin.GetAsync($"/api/drawings/{id}/content", cancellation.Token));
            Assert.True(cancellation.IsCancellationRequested);
            Assert.True(storage.SawCancellation);
            Assert.True(cancellationFailure is OperationCanceledException or HttpRequestException);
        }
        storage.BlockCopies = false;

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/drawings/{Guid.NewGuid()}/content")).StatusCode);

        var uploadingId = Guid.NewGuid();
        await factory.ExecuteDatabaseAsync(async context =>
        {
            var creator = await context.Users.Select(x => x.Id).FirstAsync();
            context.DrawingDocuments.Add(DrawingDocument.CreateUploading(uploadingId, "pending.pdf", "pending/key",
                10, new string('b', 64), 1, creator, DateTime.UtcNow));
            await context.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.GetAsync($"/api/drawings/{uploadingId}/content")).StatusCode);

        storage.RemoveAll();
        var missingObject = await admin.GetAsync($"/api/drawings/{id}/content");
        Assert.Equal(HttpStatusCode.BadGateway, missingObject.StatusCode);
        var problem = await missingObject.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("drawing.storage_object_missing", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("StorageKey", await missingObject.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        return client;
    }

    private static async Task<HttpResponseMessage> RangeAsync(HttpClient client, Guid id, string value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/drawings/{id}/content");
        request.Headers.TryAddWithoutValidation("Range", value);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-integration"u8.ToArray());
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", "drawing.pdf");
        return client.PostAsync("/api/drawings", form);
    }
}
