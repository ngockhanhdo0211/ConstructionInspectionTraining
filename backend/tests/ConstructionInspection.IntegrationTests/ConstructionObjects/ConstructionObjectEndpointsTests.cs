using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.IntegrationTests.ConstructionObjects;

public sealed class ConstructionObjectEndpointsTests(
    ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@local.test";
    private const string InspectorEmail = "inspector@local.test";
    private const string ManagerEmail = "manager@local.test";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => factory.ResetDatabaseAsync();

    [Fact]
    public async Task Endpoints_EnforceAuthenticationAndRoles()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/construction-objects")).StatusCode);

        using var inspector = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(
            inspector,
            await factory.LoginAsync(inspector, InspectorEmail));
        var forbidden = await inspector.PostAsJsonAsync(
            "/api/construction-objects",
            CreateRequest(null, "AUTH-PROJECT", "Authorization Project", "Project"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task FiveEndpoints_SupportCrudQueriesAndReadScope()
    {
        using var admin = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(
            admin,
            await factory.LoginAsync(admin, AdministratorEmail));

        var project = await CreateAsync(admin, null, "PRJ-A", "Project A", "Project");
        var building = await CreateAsync(admin, Id(project), "BLD-A", "Building A", "Building");
        var floor = await CreateAsync(admin, Id(building), "FL-A-01", "Floor A 01", "Floor");
        var room = await CreateAsync(admin, Id(floor), "ROOM-A-101", "Room A 101", "Room");
        var workItem = await CreateAsync(admin, Id(room), "WORK-A-101", "Electrical Work", "WorkItem");

        var list = await admin.GetAsync(
            $"/api/construction-objects?pageNumber=1&pageSize=2&objectType=WorkItem&parentId={Id(room)}&sortBy=code&sortDirection=desc");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listPayload = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listPayload.GetProperty("totalCount").GetInt32());
        Assert.Equal("WORK-A-101", listPayload.GetProperty("items")[0].GetProperty("code").GetString());

        var detail = await admin.GetAsync($"/api/construction-objects/{Id(room)}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailPayload = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, detailPayload.GetProperty("breadcrumb").GetArrayLength());
        Assert.True(detailPayload.GetProperty("hasChildren").GetBoolean());
        Assert.Equal(1, detailPayload.GetProperty("directChildrenCount").GetInt32());
        Assert.Equal(Id(floor), detailPayload.GetProperty("parent").GetProperty("id").GetGuid());

        var update = await admin.PutAsJsonAsync(
            $"/api/construction-objects/{Id(workItem)}",
            UpdateRequest(
                Id(room),
                "work-a-101",
                "Electrical Work Updated",
                "WorkItem",
                RowVersion(workItem)));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Electrical Work Updated", updated.GetProperty("name").GetString());
        Assert.True(updated.TryGetProperty("updatedByUserId", out _));

        var deactivate = await admin.PostAsJsonAsync(
            $"/api/construction-objects/{Id(workItem)}/deactivate",
            new { rowVersion = RowVersion(updated) });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var inspector = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(
            inspector,
            await factory.LoginAsync(inspector, InspectorEmail));
        Assert.Equal(HttpStatusCode.NotFound,
            (await inspector.GetAsync($"/api/construction-objects/{Id(workItem)}")).StatusCode);

        using var manager = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(
            manager,
            await factory.LoginAsync(manager, ManagerEmail));
        Assert.Equal(HttpStatusCode.OK,
            (await manager.GetAsync($"/api/construction-objects/{Id(workItem)}")).StatusCode);

        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await admin.DeleteAsync($"/api/construction-objects/{Id(workItem)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/construction-objects/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ConflictsAndDatabaseConstraints_ProtectInvariants()
    {
        using var admin = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(
            admin,
            await factory.LoginAsync(admin, AdministratorEmail));

        var project = await CreateAsync(admin, null, "PRJ-C", "Project C", "Project");
        var building = await CreateAsync(admin, Id(project), "BLD-C", "Building C", "Building");
        var floor = await CreateAsync(admin, Id(building), "FL-C-01", "Floor C 01", "Floor");
        var room = await CreateAsync(admin, Id(floor), "ROOM-C-101", "Room C 101", "Room");
        var workItem = await CreateAsync(admin, Id(room), "WORK-C-101", "Work C 101", "WorkItem");

        var duplicate = await admin.PostAsJsonAsync(
            "/api/construction-objects",
            CreateRequest(null, "prj-c", "Duplicate", "Project"));
        await AssertConflictCodeAsync(duplicate, "construction_object.code_duplicate");

        foreach (var invalidParent in new[] { project, building, floor })
        {
            var invalidHierarchy = await admin.PostAsJsonAsync(
                "/api/construction-objects",
                CreateRequest(Id(invalidParent), $"INVALID-{Id(invalidParent):N}", "Invalid Work", "WorkItem"));
            await AssertConflictCodeAsync(
                invalidHierarchy,
                "construction_object.invalid_parent_type");
        }

        var immutableType = await admin.PutAsJsonAsync(
            $"/api/construction-objects/{Id(workItem)}",
            UpdateRequest(Id(room), "WORK-C-101", "Work C 101", "Room", RowVersion(workItem)));
        await AssertConflictCodeAsync(
            immutableType,
            "construction_object.object_type_immutable");

        var successfulUpdate = await admin.PutAsJsonAsync(
            $"/api/construction-objects/{Id(workItem)}",
            UpdateRequest(
                Id(room),
                "WORK-C-101",
                "Work C 101 Updated",
                "WorkItem",
                RowVersion(workItem)));
        Assert.Equal(HttpStatusCode.OK, successfulUpdate.StatusCode);
        var staleUpdate = await admin.PutAsJsonAsync(
            $"/api/construction-objects/{Id(workItem)}",
            UpdateRequest(
                Id(room),
                "WORK-C-101",
                "Work C 101 Stale",
                "WorkItem",
                RowVersion(workItem)));
        await AssertConflictCodeAsync(
            staleUpdate,
            "construction_object.concurrency_conflict");

        var directDescendant = await admin.PostAsJsonAsync(
            $"/api/construction-objects/{Id(room)}/deactivate",
            new { rowVersion = RowVersion(room) });
        await AssertConflictCodeAsync(
            directDescendant,
            "construction_object.active_descendants_exist");

        await factory.ExecuteDatabaseAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ConstructionObjects] SET [IsActive] = 0 WHERE [Id] = {Id(building)}"));
        var indirectDescendant = await admin.PostAsJsonAsync(
            $"/api/construction-objects/{Id(project)}/deactivate",
            new { rowVersion = RowVersion(project) });
        await AssertConflictCodeAsync(
            indirectDescendant,
            "construction_object.active_descendants_exist");

        await factory.ExecuteDatabaseAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ConstructionObjects] SET [ParentId] = {Id(room)} WHERE [Id] = {Id(floor)}"));
        var cycle = await admin.PutAsJsonAsync(
            $"/api/construction-objects/{Id(room)}",
            UpdateRequest(Id(floor), "ROOM-C-101", "Room C 101", "Room", RowVersion(room)));
        await AssertConflictCodeAsync(cycle, "construction_object.cycle_detected");

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var adminId = project.GetProperty("createdByUserId").GetGuid();
            var exception = await Assert.ThrowsAsync<SqlException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [ConstructionObjects] ([Id], [Code], [Name], [ObjectType], [IsActive], [CreatedByUserId], [CreatedAt]) VALUES ({Guid.NewGuid()}, {"INVALID-TYPE"}, {"Invalid Type"}, {"Unsupported"}, {true}, {adminId}, {DateTime.UtcNow})"));
            Assert.Equal(547, exception.Number);
        });
    }

    private static async Task<JsonElement> CreateAsync(
        HttpClient client,
        Guid? parentId,
        string code,
        string name,
        string objectType)
    {
        var response = await client.PostAsJsonAsync(
            "/api/construction-objects",
            CreateRequest(parentId, code, name, objectType));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code.ToUpperInvariant(), payload.GetProperty("code").GetString());
        Assert.True(payload.TryGetProperty("createdByUserId", out _));
        return payload;
    }

    private static object CreateRequest(
        Guid? parentId,
        string code,
        string name,
        string objectType) => new
        {
            parentId,
            code,
            name,
            objectType,
            location = "Integration test",
            description = "SQL Server integration test"
        };

    private static object UpdateRequest(
        Guid? parentId,
        string code,
        string name,
        string objectType,
        string rowVersion) => new
        {
            parentId,
            code,
            name,
            objectType,
            location = "Integration test updated",
            description = "SQL Server integration test updated",
            rowVersion
        };

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static string RowVersion(JsonElement item) =>
        item.GetProperty("rowVersion").GetString()
        ?? throw new InvalidOperationException("A rowVersion was expected.");

    private static async Task AssertConflictCodeAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
