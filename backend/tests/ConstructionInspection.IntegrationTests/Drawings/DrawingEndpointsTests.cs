using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.IntegrationTests.ConstructionObjects;

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

    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        return client;
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
