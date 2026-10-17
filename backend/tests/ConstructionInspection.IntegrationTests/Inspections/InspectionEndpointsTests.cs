using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.Infrastructure.Persistence;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.IntegrationTests.Inspections;

public sealed class InspectionEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@local.test";
    private const string InspectorEmail = "inspector@local.test";
    private const string ReviewerEmail = "reviewer@local.test";
    private const string ManagerEmail = "manager@local.test";
    private const string Schema = """
        {"components":[{"type":"textfield","key":"summary","label":"Summary"},{"type":"number","key":"score","label":"Score","validate":{"min":0,"max":10}}]}
        """;

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => factory.ResetDatabaseAsync();

    [Fact]
    public async Task FourEndpoints_EnforceAuthenticationOwnershipAndRoleMatrix()
    {
        var fixture = await CreatePublishedFixtureAsync("ROLE");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var created = await CreateInspectionAsync(inspector, fixture, "Role inspection", new { summary = "ready" });
        var inspectionId = created.GetProperty("id").GetGuid();

        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/inspections")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/inspections/{inspectionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/inspections", CreateRequest(fixture, "Anonymous", new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/inspections/{inspectionId}/draft", DraftRequest(created, new { }))).StatusCode);

        var ownList = await ReadJsonAsync(await inspector.GetAsync("/api/inspections"));
        Assert.Equal(1, ownList.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await inspector.GetAsync($"/api/inspections/{inspectionId}")).StatusCode);

        using var manager = await CreateAuthenticatedClientAsync(ManagerEmail);
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/inspections")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/inspections/{inspectionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync($"/api/inspections/{inspectionId}")).StatusCode);
        Assert.Empty(ReadItems(await ReadJsonAsync(await reviewer.GetAsync("/api/inspections"))));
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/inspections/{inspectionId}")).StatusCode);

        foreach (var client in new[] { manager, administrator, reviewer })
        {
            await AssertProblemAsync(
                await client.PostAsJsonAsync("/api/inspections", CreateRequest(fixture, "Forbidden", new { })),
                HttpStatusCode.Forbidden);
            await AssertProblemAsync(
                await client.PutAsJsonAsync($"/api/inspections/{inspectionId}/draft", DraftRequest(created, new { })),
                HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task CreateAndSaveDraft_AreAtomicAuditedAndUseOptimisticConcurrency()
    {
        var fixture = await CreatePublishedFixtureAsync("LIFE");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var created = await CreateInspectionAsync(
            inspector, fixture, "  Initial title  ", new { summary = "initial", score = 5 });
        var id = created.GetProperty("id").GetGuid();

        Assert.StartsWith($"INS-{DateTime.UtcNow.Year}-", created.GetProperty("number").GetString(), StringComparison.Ordinal);
        Assert.Equal("Initial title", created.GetProperty("title").GetString());
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.Equal(1, created.GetProperty("currentRevision").GetProperty("revisionNumber").GetInt32());
        Assert.Equal(fixture.VersionId, created.GetProperty("template").GetProperty("versionId").GetGuid());
        Assert.NotNull((object?)created.GetProperty("rowVersion").GetString());
        Assert.NotNull((object?)created.GetProperty("processRowVersion").GetString());

        var savedResponse = await inspector.PutAsJsonAsync(
            $"/api/inspections/{id}/draft",
            DraftRequest(created, new { summary = "changed", score = 8 }));
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var saved = await ReadJsonAsync(savedResponse);
        Assert.Equal(1, saved.GetProperty("revisionNumber").GetInt32());
        Assert.Contains("changed", saved.GetProperty("dataJson").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(
            created.GetProperty("currentRevision").GetProperty("rowVersion").GetString(),
            saved.GetProperty("submissionRowVersion").GetString());

        var stale = await inspector.PutAsJsonAsync(
            $"/api/inspections/{id}/draft",
            DraftRequest(created, new { summary = "stale" }));
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "inspection.concurrency_conflict");

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var inspection = await context.Inspections.SingleAsync(x => x.Id == id);
            var submission = await context.InspectionSubmissions.SingleAsync(x => x.InspectionId == id);
            var process = await context.BusinessProcesses.SingleAsync(x => x.InspectionId == id);
            var history = await context.ProcessHistories.SingleAsync(x => x.BusinessProcessId == process.Id);
            Assert.Equal(inspection.CurrentSubmissionId, submission.Id);
            Assert.Equal(fixture.VersionId, inspection.FormTemplateVersionId);
            Assert.Equal(inspection.CreatedByUserId, submission.CreatedByUserId);
            Assert.Equal(inspection.CreatedByUserId, process.CreatedByUserId);
            Assert.Equal(inspection.CreatedByUserId, history.PerformedByUserId);
            Assert.Equal("Create", history.Action);
            Assert.Equal(submission.Id, history.SubmissionId);
            Assert.Equal(1, submission.RevisionNumber);
        });
    }

    [Fact]
    public async Task Creation_PinsLatestPublishedVersionAndAllocatesUniqueNumbersConcurrently()
    {
        var fixture = await CreatePublishedFixtureAsync("PIN");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var first = await CreateInspectionAsync(inspector, fixture, "Version one", new { summary = "one" });

        var secondVersion = await CreateAndPublishNextVersionAsync(fixture.TemplateId, Schema);
        var latestFixture = fixture with { VersionId = secondVersion };
        var requests = Enumerable.Range(1, 4)
            .Select(index => CreateInspectionAsync(inspector, latestFixture, $"Concurrent {index}", new { summary = $"item-{index}" }))
            .ToArray();
        var concurrent = await Task.WhenAll(requests);

        Assert.Equal(fixture.VersionId, first.GetProperty("template").GetProperty("versionId").GetGuid());
        Assert.All(concurrent, item =>
            Assert.Equal(secondVersion, item.GetProperty("template").GetProperty("versionId").GetGuid()));
        var numbers = concurrent.Append(first).Select(item => item.GetProperty("number").GetString()).ToArray();
        Assert.Equal(numbers.Length, numbers.Distinct(StringComparer.Ordinal).Count());

        var firstDetail = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{first.GetProperty("id").GetGuid()}"));
        Assert.Equal(1, firstDetail.GetProperty("template").GetProperty("versionNumber").GetInt32());
        Assert.Equal(fixture.VersionId, firstDetail.GetProperty("template").GetProperty("versionId").GetGuid());
    }

    [Fact]
    public async Task Create_RejectsUnavailableInputsAndInvalidDataWithoutPartialInspectionRows()
    {
        var fixture = await CreatePublishedFixtureAsync("VALID");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);

        var invalidPayloads = new object[]
        {
            CreateRequest(fixture, "Unknown key", new { unknown = true }),
            CreateRequest(fixture, "Wrong type", new { score = "ten" }),
            CreateRequest(fixture, "Out of range", new { score = 11 }),
            CreateRequest(fixture, "Executable", new { summary = "javascript:alert(1)" })
        };
        foreach (var payload in invalidPayloads)
            await AssertProblemAsync(await inspector.PostAsJsonAsync("/api/inspections", payload), HttpStatusCode.BadRequest, "inspection.form_data_invalid");

        var extraProperty = await inspector.PostAsJsonAsync("/api/inspections", new
        {
            fixture.ConstructionObjectId,
            formTemplateId = fixture.TemplateId,
            title = "Extra property",
            description = (string?)null,
            data = new { },
            unexpected = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, extraProperty.StatusCode);

        await factory.ExecuteDatabaseAsync(async context =>
        {
            Assert.Equal(0, await context.Inspections.CountAsync());
            Assert.Equal(0, await context.InspectionSubmissions.CountAsync());
            Assert.Equal(0, await context.BusinessProcesses.CountAsync());
            Assert.Equal(0, await context.ProcessHistories.CountAsync());
        });

        var unpublished = await CreateTemplateAsync("UNPUBLISHED", publish: false);
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync("/api/inspections", CreateRequest(
                new Fixture(fixture.ConstructionObjectId, unpublished.TemplateId, unpublished.VersionId),
                "No published version", new { })),
            HttpStatusCode.Conflict,
            "inspection.form_template_unavailable");

        var inactiveObject = await CreateConstructionObjectAsync("INACTIVE");
        using var admin = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var deactivated = await admin.PostAsJsonAsync(
            $"/api/construction-objects/{inactiveObject.Id}/deactivate",
            new { inactiveObject.RowVersion });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync("/api/inspections", CreateRequest(
                fixture with { ConstructionObjectId = inactiveObject.Id }, "Inactive target", new { })),
            HttpStatusCode.Conflict,
            "inspection.construction_object_inactive");
    }

    [Fact]
    public async Task DetailAndDraft_FailClosedWhenCurrentSubmissionBelongsToAnotherInspection()
    {
        var fixture = await CreatePublishedFixtureAsync("SUBMISSION");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var first = await CreateInspectionAsync(inspector, fixture, "First inspection", new { });
        var second = await CreateInspectionAsync(inspector, fixture, "Second inspection", new { });
        var firstId = first.GetProperty("id").GetGuid();
        var secondSubmissionId = second.GetProperty("currentRevision").GetProperty("id").GetGuid();

        await factory.ExecuteDatabaseAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Inspections] SET [CurrentSubmissionId] = {secondSubmissionId} WHERE [Id] = {firstId}");
        });

        await AssertProblemAsync(
            await inspector.GetAsync($"/api/inspections/{firstId}"),
            HttpStatusCode.NotFound,
            "inspection.not_found");
        await AssertProblemAsync(
            await inspector.PutAsJsonAsync($"/api/inspections/{firstId}/draft", DraftRequest(first, new { })),
            HttpStatusCode.NotFound,
            "inspection.not_found");
    }

    private async Task<Fixture> CreatePublishedFixtureAsync(string suffix)
    {
        var constructionObject = await CreateConstructionObjectAsync(suffix);
        var template = await CreateTemplateAsync(suffix, publish: true);
        return new(constructionObject.Id, template.TemplateId, template.VersionId);
    }

    private async Task<(Guid Id, string RowVersion)> CreateConstructionObjectAsync(string suffix)
    {
        using var admin = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var response = await admin.PostAsJsonAsync("/api/construction-objects", new
        {
            parentId = (Guid?)null,
            code = $"PRJ-{suffix}-{Guid.NewGuid():N}",
            name = $"Project {suffix}",
            objectType = "Project",
            location = "Integration test",
            description = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        return (payload.GetProperty("id").GetGuid(), RequiredString(payload, "rowVersion"));
    }

    private async Task<Fixture> CreateTemplateAsync(string suffix, bool publish)
    {
        using var admin = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var response = await admin.PostAsJsonAsync("/api/form-templates", new
        {
            code = $"INS-{suffix}-{Guid.NewGuid():N}",
            name = $"Inspection template {suffix}",
            description = "Integration test",
            schemaJson = Schema
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadJsonAsync(response);
        var template = created.GetProperty("template");
        var version = created.GetProperty("version");
        if (!publish)
            return new(Guid.Empty, template.GetProperty("id").GetGuid(), version.GetProperty("id").GetGuid());

        var publishResponse = await admin.PostAsJsonAsync(
            $"/api/form-templates/{template.GetProperty("id").GetGuid()}/versions/1/publish",
            new
            {
                templateRowVersion = RequiredString(template, "rowVersion"),
                versionRowVersion = RequiredString(version, "rowVersion")
            });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await ReadJsonAsync(publishResponse);
        return new(Guid.Empty, template.GetProperty("id").GetGuid(), published.GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateAndPublishNextVersionAsync(Guid templateId, string schema)
    {
        using var admin = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var versions = await ReadJsonAsync(await admin.GetAsync($"/api/form-templates/{templateId}/versions"));
        var templateRowVersion = RequiredString(versions.GetProperty("template"), "rowVersion");
        var createResponse = await admin.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions",
            new { schemaJson = schema, templateRowVersion });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = await ReadJsonAsync(createResponse);
        var publishResponse = await admin.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/2/publish",
            new
            {
                templateRowVersion = RequiredString(draft.GetProperty("template"), "rowVersion"),
                versionRowVersion = RequiredString(draft, "rowVersion")
            });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        return (await ReadJsonAsync(publishResponse)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateInspectionAsync(
        HttpClient inspector, Fixture fixture, string title, object data)
    {
        var response = await inspector.PostAsJsonAsync("/api/inspections", CreateRequest(fixture, title, data));
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created but received {(int)response.StatusCode}: {responseBody}");
        Assert.NotNull(response.Headers.Location);
        return JsonSerializer.Deserialize<JsonElement>(responseBody);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        return client;
    }

    private static object CreateRequest(Fixture fixture, string title, object data) => new
    {
        fixture.ConstructionObjectId,
        formTemplateId = fixture.TemplateId,
        title,
        description = "Inspection integration test",
        data
    };

    private static object DraftRequest(JsonElement detail, object data) => new
    {
        data,
        inspectionRowVersion = RequiredString(detail, "rowVersion"),
        submissionRowVersion = RequiredString(detail.GetProperty("currentRevision"), "rowVersion"),
        processRowVersion = RequiredString(detail, "processRowVersion")
    };

    private static IEnumerable<JsonElement> ReadItems(JsonElement page) =>
        page.GetProperty("items").EnumerateArray();

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidOperationException($"Expected '{property}'.");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        if (code is not null)
            Assert.True(
                problem.TryGetProperty("code", out var actualCode) && actualCode.GetString() == code,
                $"Expected problem code '{code}', but received: {problem.GetRawText()}");
    }

    private sealed record Fixture(Guid ConstructionObjectId, Guid TemplateId, Guid VersionId);
}
