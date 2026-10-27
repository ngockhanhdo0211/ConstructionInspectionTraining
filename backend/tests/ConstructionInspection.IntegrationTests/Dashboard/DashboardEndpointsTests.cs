using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ConstructionInspection.Api.Controllers;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Dashboard.Interfaces;
using ConstructionInspection.Application.Dashboard.Models;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using ConstructionInspection.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.IntegrationTests.Dashboard;

public sealed class DashboardEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@local.test";
    private const string ManagerEmail = "manager@local.test";
    private const string InspectorEmail = "inspector@local.test";
    private const string ReviewerEmail = "reviewer@local.test";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EndpointContract_AuthenticatesFourRoles_UsesNoStoreAndOptimizedCommandBudget()
    {
        var actions = typeof(DashboardController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .ToArray();
        Assert.Single(actions);
        Assert.Equal(["GET"], actions[0].HttpMethods);

        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/dashboard")).StatusCode);

        foreach (var (email, role, expectedScope, expectedCommands) in new[]
        {
            (AdministratorEmail, ApplicationRoles.Administrator, "Full", 1),
            (ManagerEmail, ApplicationRoles.Manager, "Full", 2),
            (InspectorEmail, ApplicationRoles.Inspector, "Own", 4),
            (ReviewerEmail, ApplicationRoles.Reviewer, "Assigned", 4)
        })
        {
            using var client = await CreateAuthenticatedClientAsync(email);
            var counter = factory.Services.GetRequiredService<SqlCommandCounter>();
            counter.Reset();

            var response = await client.GetAsync("/api/dashboard");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(expectedCommands, counter.Count);
            var payload = JsonSerializer.Deserialize<JsonElement>(body);
            Assert.Equal(expectedScope, payload.GetProperty("scopeMode").GetString());
            Assert.Contains(role, payload.GetProperty("roles").EnumerateArray()
                .Select(x => x.GetString()));
            Assert.Equal(0, payload.GetProperty("inspectionSummary").GetProperty("total").GetInt32());
            Assert.Equal(0, payload.GetProperty("checklistSummary").GetProperty("total").GetInt32());
            Assert.Equal(0, payload.GetProperty("rfiSummary").GetProperty("total").GetInt32());
            Assert.Empty(payload.GetProperty("actionQueue").EnumerateArray());
            Assert.DoesNotContain("dataJson", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("schemaJson", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("question", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storageKey", body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0,
            factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>().CallCount);
    }

    [Fact]
    public async Task MetricsScopesOverdueAndQueues_FollowAcceptedContract()
    {
        var now = new DateTime(2026, 10, 26, 6, 0, 0, DateTimeKind.Utc);
        factory.Services.GetRequiredService<TestClock>().Set(now);
        var objectId = await CreateConstructionObjectAsync();
        var templateId = await CreatePublishedTemplateAsync();
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        var otherEmail = $"dashboard-inspector-{Guid.NewGuid():N}@local.test";
        await factory.CreateUserAsync(otherEmail, "Dashboard Other Inspector", ApplicationRoles.Inspector);

        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var otherInspector = await CreateAuthenticatedClientAsync(otherEmail);

        var inspectionIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
            inspectionIds.Add(await CreateInspectionAsync(inspector, objectId, templateId, $"Inspection draft {index}"));
        foreach (var status in new[] { "Submitted", "UnderReview", "Approved", "Recheck", "Rejected" })
        {
            var id = await CreateInspectionAsync(inspector, objectId, templateId, $"Inspection {status}");
            inspectionIds.Add(id);
            await SetProcessAsync("InspectionId", id, status,
                status is "Submitted" or "UnderReview" or "Recheck" ? reviewerId : null, null, now);
        }
        var otherInspection = await CreateInspectionAsync(otherInspector, objectId, templateId, "Outside inspection");

        var checklistIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
            checklistIds.Add(await CreateChecklistAsync(inspector, objectId, $"Checklist draft {index}"));
        foreach (var status in new[] { "InProgress", "Submitted", "UnderReview", "Approved", "Recheck", "Rejected" })
        {
            var id = await CreateChecklistAsync(inspector, objectId, $"Checklist {status}");
            checklistIds.Add(id);
            await SetProcessAsync("ChecklistId", id, status,
                status is "Submitted" or "UnderReview" or "Recheck" ? reviewerId : null, null, now);
        }
        var otherChecklist = await CreateChecklistAsync(otherInspector, objectId, "Outside checklist");

        var rfiIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
            rfiIds.Add(await CreateRfiAsync(inspector, objectId, $"RFI draft {index}"));
        var overdue = await CreateRfiAsync(inspector, objectId, "RFI overdue");
        var dueNow = await CreateRfiAsync(inspector, objectId, "RFI due now");
        var future = await CreateRfiAsync(inspector, objectId, "RFI future");
        var nullDue = await CreateRfiAsync(inspector, objectId, "RFI null due");
        foreach (var (id, dueAt) in new[]
        {
            (overdue, (DateTime?)now.AddTicks(-1)),
            (dueNow, (DateTime?)now),
            (future, (DateTime?)now.AddHours(1)),
            (nullDue, (DateTime?)null)
        })
        {
            rfiIds.Add(id);
            await SetProcessAsync("RfiId", id, "Open", reviewerId, dueAt, now);
        }
        foreach (var status in new[] { "Responded", "Closed", "Cancelled" })
        {
            var id = await CreateRfiAsync(inspector, objectId, $"RFI {status}");
            rfiIds.Add(id);
            await SetProcessAsync("RfiId", id, status,
                status == "Responded" ? reviewerId : null, null, now);
        }
        var otherRfi = await CreateRfiAsync(otherInspector, objectId, "Outside RFI");

        await AssertProcessCreatorInvariantAsync();

        using var inspectorDashboard = await CreateAuthenticatedClientAsync(InspectorEmail);
        var inspectorResponse = await GetDashboardAsync(inspectorDashboard);
        AssertModule(inspectorResponse, "inspectionSummary", 11, 9, 7,
            ("draft", 6), ("submitted", 1), ("underReview", 1),
            ("approved", 1), ("recheck", 1), ("rejected", 1));
        AssertModule(inspectorResponse, "checklistSummary", 12, 10, 8,
            ("draft", 6), ("inProgress", 1), ("submitted", 1),
            ("underReview", 1), ("approved", 1), ("recheck", 1), ("rejected", 1));
        var inspectorRfi = inspectorResponse.GetProperty("rfiSummary");
        Assert.Equal(13, inspectorRfi.GetProperty("total").GetInt32());
        Assert.Equal(11, inspectorRfi.GetProperty("openWork").GetInt32());
        Assert.Equal(7, inspectorRfi.GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(4, inspectorRfi.GetProperty("awaitingResponse").GetInt32());
        Assert.Equal(1, inspectorRfi.GetProperty("awaitingInspectorAction").GetInt32());
        Assert.Equal(1, inspectorRfi.GetProperty("overdueOpen").GetInt32());
        AssertQueueLimitsAndOrdering(inspectorResponse);

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        var reviewerResponse = await GetDashboardAsync(reviewer);
        Assert.Equal(3, reviewerResponse.GetProperty("inspectionSummary").GetProperty("total").GetInt32());
        Assert.Equal(2, reviewerResponse.GetProperty("inspectionSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(3, reviewerResponse.GetProperty("checklistSummary").GetProperty("total").GetInt32());
        Assert.Equal(2, reviewerResponse.GetProperty("checklistSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(5, reviewerResponse.GetProperty("rfiSummary").GetProperty("total").GetInt32());
        Assert.Equal(4, reviewerResponse.GetProperty("rfiSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(1, reviewerResponse.GetProperty("rfiSummary").GetProperty("overdueOpen").GetInt32());
        Assert.Equal("Respond", reviewerResponse.GetProperty("actionQueue").EnumerateArray()
            .Single(x => x.GetProperty("entityId").GetGuid() == overdue)
            .GetProperty("actionKind").GetString());

        using var managerBeforeReassign = await CreateAuthenticatedClientAsync(ManagerEmail);
        var managerBeforeResponse = await GetDashboardAsync(managerBeforeReassign);
        Assert.Equal(1, managerBeforeResponse.GetProperty("rfiSummary")
            .GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal("Reassign", Assert.Single(managerBeforeResponse.GetProperty("actionQueue")
            .EnumerateArray()).GetProperty("actionKind").GetString());

        await SetProcessAsync("RfiId", overdue, "Open", await GetUserIdAsync(ManagerEmail), now.AddHours(1), now);
        var reviewerAfterReassign = await GetDashboardAsync(reviewer);
        Assert.Equal(4, reviewerAfterReassign.GetProperty("rfiSummary").GetProperty("total").GetInt32());
        Assert.DoesNotContain(overdue, reviewerAfterReassign.GetProperty("actionQueue").EnumerateArray()
            .Select(x => x.GetProperty("entityId").GetGuid()));

        using var managerAfterReassign = await CreateAuthenticatedClientAsync(ManagerEmail);
        var managerResponse = await GetDashboardAsync(managerAfterReassign);
        Assert.Equal(12, managerResponse.GetProperty("inspectionSummary").GetProperty("total").GetInt32());
        Assert.Equal(13, managerResponse.GetProperty("checklistSummary").GetProperty("total").GetInt32());
        Assert.Equal(14, managerResponse.GetProperty("rfiSummary").GetProperty("total").GetInt32());
        Assert.Equal(0, managerResponse.GetProperty("inspectionSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(0, managerResponse.GetProperty("checklistSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(0, managerResponse.GetProperty("rfiSummary").GetProperty("awaitingMyAction").GetInt32());

        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var administratorResponse = await GetDashboardAsync(administrator);
        Assert.Equal(0, administratorResponse.GetProperty("inspectionSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(0, administratorResponse.GetProperty("checklistSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Equal(0, administratorResponse.GetProperty("rfiSummary").GetProperty("awaitingMyAction").GetInt32());
        Assert.Empty(administratorResponse.GetProperty("actionQueue").EnumerateArray());

        Assert.NotEqual(Guid.Empty, otherInspection);
        Assert.NotEqual(Guid.Empty, otherChecklist);
        Assert.NotEqual(Guid.Empty, otherRfi);
        Assert.Equal(0,
            factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>().CallCount);
    }

    [Fact]
    public async Task ReadService_ObservesCancellationWithoutStorageOrMutation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardReadService>();
        var inspectorId = await scope.ServiceProvider.GetRequiredService<ConstructionInspection.Infrastructure.Persistence.ApplicationDbContext>()
            .Users.Where(x => x.NormalizedEmail == InspectorEmail.ToUpperInvariant())
            .Select(x => x.Id).SingleAsync();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(
            new DashboardReadScope(inspectorId, false, true, false, false, false,
                [ApplicationRoles.Inspector], "Own"),
            DateTime.UtcNow,
            source.Token));

        Assert.Equal(0,
            factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>().CallCount);
    }

    private static void AssertModule(JsonElement root, string property, int total, int openWork,
        int awaiting, params (string Status, int Count)[] statuses)
    {
        var summary = root.GetProperty(property);
        Assert.Equal(total, summary.GetProperty("total").GetInt32());
        Assert.Equal(openWork, summary.GetProperty("openWork").GetInt32());
        Assert.Equal(awaiting, summary.GetProperty("awaitingMyAction").GetInt32());
        foreach (var (status, count) in statuses)
            Assert.Equal(count, summary.GetProperty("statusCounts").GetProperty(status).GetInt32());
    }

    private static void AssertQueueLimitsAndOrdering(JsonElement root)
    {
        var items = root.GetProperty("actionQueue").EnumerateArray().ToArray();
        Assert.InRange(items.Length, 1, 15);
        Assert.All(items.GroupBy(x => x.GetProperty("module").GetString()),
            group => Assert.InRange(group.Count(), 1, 5));
        Assert.DoesNotContain(items, item => item.TryGetProperty("url", out _));
        Assert.DoesNotContain(items, item => item.TryGetProperty("dataJson", out _));
        Assert.DoesNotContain(items, item => item.TryGetProperty("question", out _));

        foreach (var group in items.GroupBy(x => x.GetProperty("module").GetString()))
        {
            var ids = group.Select(x => x.GetProperty("entityId").GetGuid()).ToArray();
            Assert.Equal(ids.Order().ToArray(), ids);
        }
    }

    private async Task<JsonElement> GetDashboardAsync(HttpClient client)
    {
        var counter = factory.Services.GetRequiredService<SqlCommandCounter>();
        counter.Reset();
        var response = await client.GetAsync("/api/dashboard");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Dashboard failed with {(int)response.StatusCode}: {body}");
        Assert.InRange(counter.Count, 1, 4);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("dashboard-secret-question", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard-secret-note", body, StringComparison.Ordinal);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private async Task<Guid> CreateConstructionObjectAsync()
    {
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var response = await administrator.PostAsJsonAsync("/api/construction-objects", new
        {
            parentId = (Guid?)null,
            code = $"DASH-{Guid.NewGuid():N}",
            name = "Dashboard Project",
            objectType = "Project",
            location = "Integration test",
            description = (string?)null
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePublishedTemplateAsync()
    {
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var create = await administrator.PostAsJsonAsync("/api/form-templates", new
        {
            code = $"DASH-{Guid.NewGuid():N}",
            name = "Dashboard Template",
            description = "Integration test",
            schemaJson = "{\"components\":[]}"
        });
        create.EnsureSuccessStatusCode();
        var payload = await create.Content.ReadFromJsonAsync<JsonElement>();
        var template = payload.GetProperty("template");
        var version = payload.GetProperty("version");
        var publish = await administrator.PostAsJsonAsync(
            $"/api/form-templates/{template.GetProperty("id").GetGuid()}/versions/1/publish", new
            {
                templateRowVersion = template.GetProperty("rowVersion").GetString(),
                versionRowVersion = version.GetProperty("rowVersion").GetString()
            });
        publish.EnsureSuccessStatusCode();
        return template.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateInspectionAsync(
        HttpClient client, Guid objectId, Guid templateId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/inspections", new
        {
            constructionObjectId = objectId,
            formTemplateId = templateId,
            title,
            description = (string?)null,
            data = new { }
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateChecklistAsync(HttpClient client, Guid objectId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/checklists", new
        {
            constructionObjectId = objectId,
            title,
            description = (string?)null,
            items = new[] { new { criterion = "dashboard-secret-note", isRequired = true } }
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checklistId").GetGuid();
    }

    private static async Task<Guid> CreateRfiAsync(HttpClient client, Guid objectId, string subject)
    {
        var response = await client.PostAsJsonAsync("/api/rfis", new
        {
            constructionObjectId = objectId,
            subject,
            question = "dashboard-secret-question"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rfiId").GetGuid();
    }

    private Task SetProcessAsync(string ownerColumn, Guid ownerId, string status,
        Guid? assignedTo, DateTime? dueAt, DateTime updatedAt) =>
        factory.ExecuteDatabaseAsync(context => ownerColumn switch
        {
            "InspectionId" => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BusinessProcesses] SET [Status]={status}, [AssignedToUserId]={assignedTo}, [DueAt]={dueAt}, [UpdatedAt]={updatedAt} WHERE [InspectionId]={ownerId}"),
            "ChecklistId" => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BusinessProcesses] SET [Status]={status}, [AssignedToUserId]={assignedTo}, [DueAt]={dueAt}, [UpdatedAt]={updatedAt} WHERE [ChecklistId]={ownerId}"),
            "RfiId" => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BusinessProcesses] SET [Status]={status}, [AssignedToUserId]={assignedTo}, [DueAt]={dueAt}, [UpdatedAt]={updatedAt} WHERE [RfiId]={ownerId}"),
            _ => throw new InvalidOperationException("Unknown process owner column.")
        });

    private async Task<Guid> GetUserIdAsync(string email)
    {
        var id = Guid.Empty;
        await factory.ExecuteDatabaseAsync(async context =>
            id = await context.Users.Where(x => x.NormalizedEmail == email.ToUpperInvariant())
                .Select(x => x.Id).SingleAsync());
        return id;
    }

    private Task AssertProcessCreatorInvariantAsync() => factory.ExecuteDatabaseAsync(async context =>
    {
        Assert.False(await context.BusinessProcesses
            .Where(process => process.InspectionId != null)
            .Join(context.Inspections, process => process.InspectionId, inspection => inspection.Id,
                (process, inspection) => new { process.CreatedByUserId, OwnerCreatedBy = inspection.CreatedByUserId })
            .AnyAsync(row => row.CreatedByUserId != row.OwnerCreatedBy));
        Assert.False(await context.BusinessProcesses
            .Where(process => process.ChecklistId != null)
            .Join(context.Checklists, process => process.ChecklistId, checklist => checklist.Id,
                (process, checklist) => new { process.CreatedByUserId, OwnerCreatedBy = checklist.CreatedByUserId })
            .AnyAsync(row => row.CreatedByUserId != row.OwnerCreatedBy));
        Assert.False(await context.BusinessProcesses
            .Where(process => process.RfiId != null)
            .Join(context.Rfis, process => process.RfiId, rfi => rfi.Id,
                (process, rfi) => new { process.CreatedByUserId, OwnerCreatedBy = rfi.CreatedByUserId })
            .AnyAsync(row => row.CreatedByUserId != row.OwnerCreatedBy));
    });

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        return client;
    }
}
