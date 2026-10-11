using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.Drawings;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructionInspection.IntegrationTests.DrawingMappings;

public sealed class DrawingObjectMappingEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_HasFilteredUniqueIndexAndNoActionForeignKeys()
    {
        await factory.ExecuteDatabaseAsync(async db =>
        {
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied, x => x.EndsWith("_AddDrawingObjectMappings", StringComparison.Ordinal));
            var entity = db.Model.FindEntityType(typeof(DrawingObjectMapping));
            Assert.NotNull(entity);
            var unique = entity.GetIndexes().Single(x => x.GetDatabaseName() == "UX_DrawingObjectMappings_ActiveExact");
            Assert.True(unique.IsUnique);
            Assert.Equal("[Status] = 'Active'", unique.GetFilter());
            Assert.All(entity.GetForeignKeys(), x => Assert.Equal(DeleteBehavior.NoAction, x.DeleteBehavior));
        });
    }

    [Fact]
    public async Task MappingCrud_EnforcesRolesLifecycleValidationAndDoesNotCallStorage()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/drawings/{Guid.NewGuid()}/mappings?pageNumber=1")).StatusCode);
        foreach (var email in new[] { "inspector@local.test", "reviewer@local.test" })
        {
            using var denied = await ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await denied.GetAsync($"/api/drawings/{Guid.NewGuid()}/mappings?pageNumber=1")).StatusCode);
        }

        using var admin = await ClientAsync("administrator@local.test");
        using var manager = await ClientAsync("manager@local.test");
        var drawingId = await UploadAsync(admin);
        var objectPayload = await CreateObjectAsync(admin, "MAP-PROJECT-1");
        var objectId = objectPayload.GetProperty("id").GetGuid();
        var storage = factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>();
        var storageCalls = storage.CallCount;

        Assert.Equal(HttpStatusCode.OK,
            (await manager.GetAsync($"/api/drawings/{drawingId}/mappings?pageNumber=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId))).StatusCode);

        var create = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings",
            CreateBody(objectId, 0.12345649m, 0.8m));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var mapping = await create.Content.ReadFromJsonAsync<JsonElement>();
        var mappingId = mapping.GetProperty("id").GetGuid();
        Assert.Equal(0.123456m, mapping.GetProperty("geometry").GetProperty("left").GetDecimal());
        Assert.Equal(storageCalls, storage.CallCount);

        var duplicate = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings",
            CreateBody(objectId, 0.12345649m, 0.8m));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("drawing_mapping.duplicate", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var near = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings",
            CreateBody(objectId, 0.1234566m, 0.8m));
        Assert.Equal(HttpStatusCode.Created, near.StatusCode);

        var invalidPage = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId, pageNumber: 99));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        var invalidGeometry = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings",
            CreateBody(objectId, left: 0.5m, right: 0.5m));
        Assert.Equal(HttpStatusCode.BadRequest, invalidGeometry.StatusCode);

        var managerSources = await manager.GetAsync($"/api/construction-objects/{objectId}/drawing-mappings");
        Assert.Equal(HttpStatusCode.OK, managerSources.StatusCode);
        Assert.Equal(2, (await managerSources.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("mappings").GetProperty("totalCount").GetInt32());

        var rowVersion = mapping.GetProperty("rowVersion").GetString()!;
        var update = await admin.PutAsJsonAsync($"/api/drawing-object-mappings/{mappingId}", new
        {
            constructionObjectId = objectId,
            geometry = new { left = 0.2m, bottom = 0.2m, right = 0.6m, top = 0.8m },
            rowVersion
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/drawing-object-mappings/{mappingId}", new
        {
            constructionObjectId = objectId,
            geometry = new { left = 0.25m, bottom = 0.2m, right = 0.65m, top = 0.8m },
            rowVersion
        })).StatusCode);

        var deactivate = await admin.PostAsJsonAsync($"/api/drawing-object-mappings/{mappingId}/deactivate",
            new { rowVersion = updated.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/drawings/{drawingId}/mappings?pageNumber=1");
        Assert.Single(list.GetProperty("items").EnumerateArray());
        await factory.ExecuteDatabaseAsync(async db =>
            Assert.NotNull(await db.DrawingObjectMappings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mappingId)));
        Assert.Equal(storageCalls, storage.CallCount);
    }

    [Fact]
    public async Task AtomicCreate_RollsBackAndObjectInactiveAndDrawingStateAreEnforced()
    {
        using var admin = await ClientAsync("administrator@local.test");
        var drawingId = await UploadAsync(admin);
        var failed = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings/with-construction-object",
            AtomicBody("ATOMIC-ROLLBACK", 99));
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        await factory.ExecuteDatabaseAsync(async db =>
            Assert.False(await db.ConstructionObjects.AnyAsync(x => x.Code == "ATOMIC-ROLLBACK")));

        var created = await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings/with-construction-object",
            AtomicBody("ATOMIC-SUCCESS", 1));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var atomic = await created.Content.ReadFromJsonAsync<JsonElement>();
        var objectResult = atomic.GetProperty("constructionObject");
        var objectId = objectResult.GetProperty("id").GetGuid();
        var mappingId = atomic.GetProperty("mapping").GetProperty("id").GetGuid();

        var deactivateObject = await admin.PostAsJsonAsync($"/api/construction-objects/{objectId}/deactivate",
            new { rowVersion = objectResult.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, deactivateObject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId, 0.5m, 0.9m))).StatusCode);
        var source = await admin.GetFromJsonAsync<JsonElement>($"/api/construction-objects/{objectId}/drawing-mappings");
        Assert.False(source.GetProperty("constructionObjectIsActive").GetBoolean());
        Assert.Equal(mappingId, source.GetProperty("mappings").GetProperty("items")[0].GetProperty("mappingId").GetGuid());

        await factory.ExecuteDatabaseAsync(async db =>
        {
            var creator = await db.Users.Select(x => x.Id).FirstAsync();
            var pending = DrawingDocument.CreateUploading(Guid.NewGuid(), "pending.pdf", "pending/mapping",
                10, new string('c', 64), 1, creator, DateTime.UtcNow);
            db.DrawingDocuments.Add(pending);
            await db.SaveChangesAsync();
            var response = await admin.PostAsJsonAsync($"/api/drawings/{pending.Id}/mappings",
                CreateBody(objectId));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        });
    }

    [Fact]
    public async Task ConcurrentCreate_ProtectsExactDuplicateAndCapacityBoundary()
    {
        using var admin = await ClientAsync("administrator@local.test");
        var drawingId = await UploadAsync(admin);
        var objectId = (await CreateObjectAsync(admin, "CONCURRENT-PROJECT")).GetProperty("id").GetGuid();

        var duplicateRequests = new[]
        {
            admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId)),
            admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId))
        };
        var duplicateResults = await Task.WhenAll(duplicateRequests);
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
            duplicateResults.Select(x => x.StatusCode).OrderBy(x => x).ToArray());

        await factory.ExecuteDatabaseAsync(async db =>
        {
            var actor = await db.Users.Select(x => x.Id).FirstAsync();
            var existing = await db.DrawingObjectMappings.CountAsync(x =>
                x.DrawingDocumentId == drawingId && x.PageNumber == 2);
            Assert.Equal(0, existing);
            for (var i = 0; i < 499; i++)
            {
                var left = i / 1000m;
                db.DrawingObjectMappings.Add(DrawingObjectMapping.Create(Guid.NewGuid(), drawingId, 2,
                    objectId, DrawingMappingShapeType.Rectangle,
                    DrawingRectangle.Create(left, 0.1m, left + 0.0005m, 0.2m), actor, DateTime.UtcNow));
            }
            await db.SaveChangesAsync();
        });

        var capacityResults = await Task.WhenAll(
            admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId, 0.7m, 0.7005m, 2)),
            admin.PostAsJsonAsync($"/api/drawings/{drawingId}/mappings", CreateBody(objectId, 0.8m, 0.8005m, 2)));
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
            capacityResults.Select(x => x.StatusCode).OrderBy(x => x).ToArray());
        await factory.ExecuteDatabaseAsync(async db => Assert.Equal(500,
            await db.DrawingObjectMappings.CountAsync(x => x.DrawingDocumentId == drawingId
                && x.PageNumber == 2 && x.Status == DrawingObjectMappingStatus.Active)));
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        return client;
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-mapping"u8.ToArray()), "file", "mapping.pdf");
        var response = await client.PostAsync("/api/drawings", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("drawing").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateObjectAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/construction-objects", new
        { parentId = (Guid?)null, code, name = code, objectType = "Project", location = (string?)null, description = (string?)null });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object CreateBody(Guid objectId, decimal left = 0.1m, decimal right = 0.5m, int pageNumber = 1) => new
    {
        pageNumber,
        constructionObjectId = objectId,
        shapeType = "Rectangle",
        geometry = new { left, bottom = 0.2m, right, top = 0.8m }
    };

    private static object AtomicBody(string code, int pageNumber) => new
    {
        pageNumber,
        shapeType = "Rectangle",
        geometry = new { left = 0.1m, bottom = 0.1m, right = 0.2m, top = 0.2m },
        parentId = (Guid?)null,
        code,
        name = code,
        objectType = "Project",
        location = (string?)null,
        description = (string?)null
    };
}
