using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.Application.Common.Authorization;
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
    public async Task CreationLookups_ExposeScopedObjectsAndOnlyActiveTemplatesWithLatestPublishedVersion()
    {
        var activeObject = await CreateConstructionObjectAsync("LK-ACT");
        var inactiveObject = await CreateConstructionObjectAsync("LK-INACT");
        var usable = await CreateTemplateAsync("LK-USE", publish: true);
        var latestVersionId = await CreateAndPublishNextVersionAsync(usable.TemplateId, Schema);
        _ = await CreateTemplateAsync("LK-DRAFT", publish: false);
        var inactiveTemplate = await CreateTemplateAsync("LK-INACT", publish: true);

        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        Assert.Equal(HttpStatusCode.OK, (await administrator.PostAsJsonAsync(
            $"/api/construction-objects/{inactiveObject.Id}/deactivate",
            new { inactiveObject.RowVersion })).StatusCode);
        var inactiveTemplateVersions = await ReadJsonAsync(
            await administrator.GetAsync($"/api/form-templates/{inactiveTemplate.TemplateId}/versions"));
        Assert.Equal(HttpStatusCode.OK, (await administrator.PostAsJsonAsync(
            $"/api/form-templates/{inactiveTemplate.TemplateId}/deactivate",
            new { rowVersion = RequiredString(inactiveTemplateVersions.GetProperty("template"), "rowVersion") })).StatusCode);

        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var objectPage = await ReadJsonAsync(await inspector.GetAsync(
            "/api/construction-objects?search=LK-&pageNumber=1&pageSize=100"));
        var objectIds = ReadItems(objectPage).Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(activeObject.Id, objectIds);
        Assert.DoesNotContain(inactiveObject.Id, objectIds);

        var optionsResponse = await inspector.GetAsync(
            "/api/inspections/form-template-options?search=LK-&pageNumber=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        var options = await ReadJsonAsync(optionsResponse);
        Assert.Equal(1, options.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, options.GetProperty("totalPages").GetInt32());
        var option = Assert.Single(ReadItems(options));
        Assert.Equal(usable.TemplateId, option.GetProperty("formTemplateId").GetGuid());
        Assert.Equal(2, option.GetProperty("latestPublishedVersionNumber").GetInt32());
        Assert.Equal(
            ["code", "formTemplateId", "latestPublishedVersionNumber", "name"],
            option.EnumerateObject().Select(property => property.Name).Order().ToArray());

        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/inspections/form-template-options")).StatusCode);
        using var manager = await CreateAuthenticatedClientAsync(ManagerEmail);
        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        foreach (var client in new[] { administrator, manager, reviewer })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/inspections/form-template-options")).StatusCode);

        var created = await CreateInspectionAsync(
            inspector,
            new(activeObject.Id, usable.TemplateId, latestVersionId),
            "Lookup-backed inspection",
            new { summary = "ready" });
        Assert.Equal(2, created.GetProperty("template").GetProperty("versionNumber").GetInt32());
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

    [Fact]
    public async Task ReviewerLookup_EnforcesRolesFiltersIdentityAndReturnsMinimalStablePage()
    {
        var secondEmail = $"reviewer.second.{Guid.NewGuid():N}@local.test";
        var secondId = await factory.CreateUserAsync(secondEmail, "A Second Reviewer", ApplicationRoles.Reviewer);
        var inactiveEmail = $"reviewer.inactive.{Guid.NewGuid():N}@local.test";
        _ = await factory.CreateUserAsync(inactiveEmail, "Z Inactive Reviewer", ApplicationRoles.Reviewer);
        await factory.SetUserActiveAsync(inactiveEmail, false);

        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var manager = await CreateAuthenticatedClientAsync(ManagerEmail);
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        foreach (var client in new[] { inspector, manager, administrator })
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync("/api/inspections/reviewer-options?pageNumber=1&pageSize=20")).StatusCode);

        var page = await ReadJsonAsync(await inspector.GetAsync(
            "/api/inspections/reviewer-options?pageNumber=1&pageSize=1&search=Second"));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(ReadItems(page));
        Assert.Equal(secondId, item.GetProperty("userId").GetGuid());
        Assert.Equal(["displayName", "userId"],
            item.EnumerateObject().Select(x => x.Name).Order().ToArray());

        var all = await ReadJsonAsync(await inspector.GetAsync(
            "/api/inspections/reviewer-options?pageNumber=1&pageSize=100"));
        Assert.DoesNotContain(ReadItems(all), x =>
            string.Equals(x.GetProperty("displayName").GetString(), "Z Inactive Reviewer", StringComparison.Ordinal));

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reviewer.GetAsync("/api/inspections/reviewer-options")).StatusCode);
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/inspections/reviewer-options")).StatusCode);
    }

    [Fact]
    public async Task Submit_ValidatesRequiredDataReviewerEligibilityAndAllConcurrencyTokens()
    {
        const string requiredSchema = """
            {"components":[{"type":"textfield","key":"summary","label":"Summary","validate":{"required":true}}]}
            """;
        var fixture = await CreatePublishedFixtureAsync("SUBMIT", requiredSchema);
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);

        var empty = await CreateInspectionAsync(inspector, fixture, "Missing required", new { });
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync($"/api/inspections/{empty.GetProperty("id").GetGuid()}/submit",
                SubmitRequest(empty, reviewerId)),
            HttpStatusCode.BadRequest,
            "inspection.form_data_invalid");

        var saved = await inspector.PutAsJsonAsync(
            $"/api/inspections/{empty.GetProperty("id").GetGuid()}/draft",
            DraftRequest(empty, new { summary = "complete" }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var detail = await ReadJsonAsync(await inspector.GetAsync(
            $"/api/inspections/{empty.GetProperty("id").GetGuid()}"));
        var submitRequest = SubmitRequest(detail, reviewerId);
        var submittedResponse = await inspector.PostAsJsonAsync(
            $"/api/inspections/{detail.GetProperty("id").GetGuid()}/submit", submitRequest);
        Assert.Equal(HttpStatusCode.OK, submittedResponse.StatusCode);
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync(
                $"/api/inspections/{detail.GetProperty("id").GetGuid()}/submit", submitRequest),
            HttpStatusCode.Conflict);
        await AssertProblemAsync(
            await inspector.PutAsJsonAsync(
                $"/api/inspections/{detail.GetProperty("id").GetGuid()}/draft", DraftRequest(detail, new { summary = "changed" })),
            HttpStatusCode.Conflict);

        foreach (var stale in new[] { "inspection", "submission", "process" })
        {
            var candidate = await CreateInspectionAsync(
                inspector, fixture, $"Stale {stale}", new { summary = "valid" });
            await AssertProblemAsync(
                await inspector.PostAsJsonAsync(
                    $"/api/inspections/{candidate.GetProperty("id").GetGuid()}/submit",
                    SubmitRequest(candidate, reviewerId, stale)),
                HttpStatusCode.Conflict,
                "inspection.concurrency_conflict");
        }

        var inactive = await CreateInspectionAsync(inspector, fixture, "Inactive reviewer", new { summary = "valid" });
        await factory.SetUserActiveAsync(ReviewerEmail, false);
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync(
                $"/api/inspections/{inactive.GetProperty("id").GetGuid()}/submit", SubmitRequest(inactive, reviewerId)),
            HttpStatusCode.Conflict,
            "inspection.reviewer_invalid");
        await factory.SetUserActiveAsync(ReviewerEmail, true);

        var noRoleEmail = $"reviewer.norole.{Guid.NewGuid():N}@local.test";
        var noRoleId = await factory.CreateUserAsync(noRoleEmail, "Role Removed", ApplicationRoles.Reviewer);
        await factory.RemoveUserRoleAsync(noRoleEmail, ApplicationRoles.Reviewer);
        var noRole = await CreateInspectionAsync(inspector, fixture, "No role", new { summary = "valid" });
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync(
                $"/api/inspections/{noRole.GetProperty("id").GetGuid()}/submit", SubmitRequest(noRole, noRoleId)),
            HttpStatusCode.Conflict,
            "inspection.reviewer_invalid");

        var selfId = await GetUserIdAsync(InspectorEmail);
        await factory.ExecuteDatabaseAsync(async context =>
        {
            var reviewerRoleId = await context.Roles.Where(x => x.NormalizedName == ApplicationRoles.Reviewer)
                .Select(x => x.Id).SingleAsync();
            context.UserRoles.Add(new() { UserId = selfId, RoleId = reviewerRoleId });
            await context.SaveChangesAsync();
        });
        var selfReview = await CreateInspectionAsync(inspector, fixture, "Self review", new { summary = "valid" });
        await AssertProblemAsync(
            await inspector.PostAsJsonAsync(
                $"/api/inspections/{selfReview.GetProperty("id").GetGuid()}/submit", SubmitRequest(selfReview, selfId)),
            HttpStatusCode.Conflict,
            "inspection.self_review_forbidden");
    }

    [Fact]
    public async Task ReviewDecisions_RequireAssigneeStartReviewAndOnlyOneConcurrentDecisionWins()
    {
        var fixture = await CreatePublishedFixtureAsync("DECISION");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        var otherEmail = $"reviewer.other.{Guid.NewGuid():N}@local.test";
        _ = await factory.CreateUserAsync(otherEmail, "Other Reviewer", ApplicationRoles.Reviewer);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        using var otherReviewer = await CreateAuthenticatedClientAsync(otherEmail);
        var created = await CreateInspectionAsync(inspector, fixture, "Concurrent decision", new { summary = "ready" });
        var submitted = await ReadJsonAsync(await inspector.PostAsJsonAsync(
            $"/api/inspections/{created.GetProperty("id").GetGuid()}/submit", SubmitRequest(created, reviewerId)));
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await otherReviewer.GetAsync($"/api/inspections/{id}")).StatusCode);
        await AssertProblemAsync(
            await reviewer.PostAsJsonAsync($"/api/inspections/{id}/approve",
                new { comment = "premature", processRowVersion = RequiredString(submitted, "processRowVersion") }),
            HttpStatusCode.Conflict);
        var startPayload = new { processRowVersion = RequiredString(submitted, "processRowVersion") };
        var startedResponse = await reviewer.PostAsJsonAsync($"/api/inspections/{id}/start-review", startPayload);
        Assert.Equal(HttpStatusCode.OK, startedResponse.StatusCode);
        await AssertProblemAsync(
            await reviewer.PostAsJsonAsync($"/api/inspections/{id}/start-review", startPayload),
            HttpStatusCode.Conflict);
        var started = await ReadJsonAsync(startedResponse);
        var token = RequiredString(started, "processRowVersion");

        var decisions = await Task.WhenAll(
            reviewer.PostAsJsonAsync($"/api/inspections/{id}/approve", new { comment = "approved", processRowVersion = token }),
            reviewer.PostAsJsonAsync($"/api/inspections/{id}/reject", new { reason = "rejected", processRowVersion = token }));
        Assert.Equal(1, decisions.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, decisions.Count(x => x.StatusCode == HttpStatusCode.Conflict));

        var history = await ReadJsonAsync(await reviewer.GetAsync($"/api/inspections/{id}/history?pageSize=100"));
        var terminal = ReadItems(history).Count(x =>
            x.GetProperty("action").GetString() is "Approve" or "Reject");
        Assert.Equal(1, terminal);
    }

    [Fact]
    public async Task Recheck_FirstSaveCreatesOneRevisionAndResubmitKeepsPinnedTemplateAndReviewer()
    {
        var fixture = await CreatePublishedFixtureAsync("RECHECK");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        var created = await CreateInspectionAsync(inspector, fixture, "Recheck lifecycle", new { summary = "revision-one" });
        var id = created.GetProperty("id").GetGuid();
        var submitted = await ReadJsonAsync(await inspector.PostAsJsonAsync(
            $"/api/inspections/{id}/submit", SubmitRequest(created, reviewerId)));
        var started = await ReadJsonAsync(await reviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/start-review",
            new { processRowVersion = RequiredString(submitted, "processRowVersion") }));
        var rechecked = await ReadJsonAsync(await reviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/recheck",
            new { reason = "Correct the summary", processRowVersion = RequiredString(started, "processRowVersion") }));

        var beforeSave = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}/revisions?pageSize=100"));
        Assert.Single(ReadItems(beforeSave));
        var recheckDetail = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}"));
        var firstSave = await inspector.PutAsJsonAsync(
            $"/api/inspections/{id}/draft", DraftRequest(recheckDetail, new { summary = "revision-two" }));
        Assert.Equal(HttpStatusCode.OK, firstSave.StatusCode);
        var firstSaved = await ReadJsonAsync(firstSave);
        Assert.Equal(2, firstSaved.GetProperty("revisionNumber").GetInt32());

        var afterFirst = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}"));
        var secondSave = await inspector.PutAsJsonAsync(
            $"/api/inspections/{id}/draft", DraftRequest(afterFirst, new { summary = "revision-two-updated" }));
        Assert.Equal(HttpStatusCode.OK, secondSave.StatusCode);
        Assert.Equal(2, (await ReadJsonAsync(secondSave)).GetProperty("revisionNumber").GetInt32());

        var revisions = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}/revisions?pageSize=100"));
        Assert.Equal(2, ReadItems(revisions).Count());
        Assert.All(ReadItems(revisions), item => Assert.False(item.TryGetProperty("dataJson", out _)));
        var revisionOne = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}/revisions/1"));
        var revisionTwo = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}/revisions/2"));
        Assert.Contains("revision-one", revisionOne.GetProperty("dataJson").GetString(), StringComparison.Ordinal);
        Assert.Contains("revision-two-updated", revisionTwo.GetProperty("dataJson").GetString(), StringComparison.Ordinal);
        Assert.Equal(fixture.VersionId, revisionTwo.GetProperty("template").GetProperty("versionId").GetGuid());

        var latest = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}"));
        var resubmittedResponse = await inspector.PostAsJsonAsync(
            $"/api/inspections/{id}/submit", SubmitRequest(latest, reviewerId));
        Assert.Equal(HttpStatusCode.OK, resubmittedResponse.StatusCode);
        var resubmitted = await ReadJsonAsync(resubmittedResponse);
        Assert.Equal(reviewerId, resubmitted.GetProperty("reviewerUserId").GetGuid());
        Assert.Equal("Submitted", resubmitted.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/start-review",
            new { processRowVersion = RequiredString(resubmitted, "processRowVersion") })).StatusCode);

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var inspection = await context.Inspections.SingleAsync(x => x.Id == id);
            Assert.Equal(3, inspection.NextRevisionNumber);
            Assert.Equal(fixture.VersionId, inspection.FormTemplateVersionId);
            Assert.Equal(2, await context.InspectionSubmissions.CountAsync(x => x.InspectionId == id));
            Assert.Equal(1, await context.ProcessHistories.CountAsync(x =>
                x.BusinessProcessId == context.BusinessProcesses.Where(p => p.InspectionId == id).Select(p => p.Id).Single()
                && x.Action == "CreateRevision"));
        });
    }

    [Fact]
    public async Task ConcurrentFirstRecheckSaveCreatesExactlyOneRevision()
    {
        var fixture = await CreatePublishedFixtureAsync("RECHECK-RACE");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        var created = await CreateInspectionAsync(inspector, fixture, "Recheck race", new { summary = "one" });
        var id = created.GetProperty("id").GetGuid();
        var submitted = await ReadJsonAsync(await inspector.PostAsJsonAsync(
            $"/api/inspections/{id}/submit", SubmitRequest(created, reviewerId)));
        var started = await ReadJsonAsync(await reviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/start-review",
            new { processRowVersion = RequiredString(submitted, "processRowVersion") }));
        _ = await reviewer.PostAsJsonAsync($"/api/inspections/{id}/recheck",
            new { reason = "Retry", processRowVersion = RequiredString(started, "processRowVersion") });
        var detail = await ReadJsonAsync(await inspector.GetAsync($"/api/inspections/{id}"));
        var payload = DraftRequest(detail, new { summary = "two" });

        var results = await Task.WhenAll(
            inspector.PutAsJsonAsync($"/api/inspections/{id}/draft", payload),
            inspector.PutAsJsonAsync($"/api/inspections/{id}/draft", payload));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await factory.ExecuteDatabaseAsync(async context =>
        {
            Assert.Equal(2, await context.InspectionSubmissions.CountAsync(x => x.InspectionId == id));
            Assert.Equal(3, await context.Inspections.Where(x => x.Id == id).Select(x => x.NextRevisionNumber).SingleAsync());
        });
    }

    [Fact]
    public async Task Reassign_EnforcesRoleStateScopeAndStructuredReviewerAudit()
    {
        var fixture = await CreatePublishedFixtureAsync("REASSIGN");
        var originalReviewerId = await GetUserIdAsync(ReviewerEmail);
        var replacementEmail = $"reviewer.replacement.{Guid.NewGuid():N}@local.test";
        var replacementId = await factory.CreateUserAsync(
            replacementEmail, "Replacement Reviewer", ApplicationRoles.Reviewer);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var originalReviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        using var replacementReviewer = await CreateAuthenticatedClientAsync(replacementEmail);
        using var manager = await CreateAuthenticatedClientAsync(ManagerEmail);
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var created = await CreateInspectionAsync(inspector, fixture, "Reassign lifecycle", new { summary = "ready" });
        var id = created.GetProperty("id").GetGuid();
        var submitted = await ReadJsonAsync(await inspector.PostAsJsonAsync(
            $"/api/inspections/{id}/submit", SubmitRequest(created, originalReviewerId)));
        var request = new
        {
            reviewerUserId = replacementId,
            reason = "Balance workload",
            inspectionRowVersion = RequiredString(submitted, "inspectionRowVersion"),
            processRowVersion = RequiredString(submitted, "processRowVersion")
        };

        Assert.Equal(HttpStatusCode.Forbidden,
            (await inspector.PostAsJsonAsync($"/api/inspections/{id}/reassign-reviewer", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await originalReviewer.PostAsJsonAsync($"/api/inspections/{id}/reassign-reviewer", request)).StatusCode);
        await AssertProblemAsync(
            await manager.PostAsJsonAsync($"/api/inspections/{id}/reassign-reviewer", new
            {
                reviewerUserId = replacementId,
                reason = "",
                inspectionRowVersion = RequiredString(submitted, "inspectionRowVersion"),
                processRowVersion = RequiredString(submitted, "processRowVersion")
            }), HttpStatusCode.BadRequest);
        await AssertProblemAsync(
            await manager.PostAsJsonAsync($"/api/inspections/{id}/reassign-reviewer", new
            {
                reviewerUserId = Guid.NewGuid(),
                reason = "Invalid reviewer",
                inspectionRowVersion = RequiredString(submitted, "inspectionRowVersion"),
                processRowVersion = RequiredString(submitted, "processRowVersion")
            }), HttpStatusCode.Conflict, "inspection.reviewer_invalid");

        var reassignedResponse = await manager.PostAsJsonAsync(
            $"/api/inspections/{id}/reassign-reviewer", request);
        Assert.Equal(HttpStatusCode.OK, reassignedResponse.StatusCode);
        var reassigned = await ReadJsonAsync(reassignedResponse);
        Assert.Equal("Submitted", reassigned.GetProperty("status").GetString());
        Assert.Equal(replacementId, reassigned.GetProperty("reviewerUserId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await originalReviewer.GetAsync($"/api/inspections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await replacementReviewer.GetAsync($"/api/inspections/{id}")).StatusCode);

        var replacementStarted = await ReadJsonAsync(await replacementReviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/start-review",
            new { processRowVersion = RequiredString(reassigned, "processRowVersion") }));
        var adminReassignedResponse = await administrator.PostAsJsonAsync(
            $"/api/inspections/{id}/reassign-reviewer", new
            {
                reviewerUserId = originalReviewerId,
                reason = "Return assignment",
                inspectionRowVersion = RequiredString(replacementStarted, "inspectionRowVersion"),
                processRowVersion = RequiredString(replacementStarted, "processRowVersion")
            });
        Assert.Equal(HttpStatusCode.OK, adminReassignedResponse.StatusCode);
        var adminReassigned = await ReadJsonAsync(adminReassignedResponse);
        Assert.Equal("Submitted", adminReassigned.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await replacementReviewer.GetAsync($"/api/inspections/{id}")).StatusCode);

        var history = await ReadJsonAsync(await administrator.GetAsync($"/api/inspections/{id}/history?pageSize=100"));
        var reassignments = ReadItems(history).Where(x =>
            x.GetProperty("action").GetString() == "ReassignReviewer").ToArray();
        Assert.Equal(2, reassignments.Length);
        Assert.Contains(reassignments, x =>
            x.GetProperty("previousReviewerUserId").GetGuid() == originalReviewerId
            && x.GetProperty("newReviewerUserId").GetGuid() == replacementId
            && x.GetProperty("reason").GetString() == "Balance workload");
        Assert.Contains(reassignments, x =>
            x.GetProperty("previousReviewerUserId").GetGuid() == replacementId
            && x.GetProperty("newReviewerUserId").GetGuid() == originalReviewerId);

        var originalStarted = await ReadJsonAsync(await originalReviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/start-review",
            new { processRowVersion = RequiredString(adminReassigned, "processRowVersion") }));
        var approved = await ReadJsonAsync(await originalReviewer.PostAsJsonAsync(
            $"/api/inspections/{id}/approve",
            new { comment = "Complete", processRowVersion = RequiredString(originalStarted, "processRowVersion") }));
        await AssertProblemAsync(
            await administrator.PostAsJsonAsync($"/api/inspections/{id}/reassign-reviewer", new
            {
                reviewerUserId = replacementId,
                reason = "Too late",
                inspectionRowVersion = RequiredString(approved, "inspectionRowVersion"),
                processRowVersion = RequiredString(approved, "processRowVersion")
            }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DatabaseContract_HasWorkflowMigrationConstraintsIndexesAndNoActionForeignKeys()
    {
        await factory.ExecuteDatabaseAsync(async context =>
        {
            var migrations = await context.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261018123538_AddInspectionWorkflow", migrations);
            Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM sys.columns c
                JOIN sys.tables t ON c.object_id = t.object_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                WHERE t.name = 'Inspections' AND c.name = 'NextRevisionNumber'
                  AND c.is_nullable = 0 AND dc.object_id IS NULL
                """).SingleAsync());
            Assert.Equal(4, await context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM sys.check_constraints
                WHERE name IN ('CK_Inspections_NextRevisionNumber','CK_BusinessProcesses_Status',
                               'CK_ProcessHistories_ToStatus','CK_ProcessHistories_Action')
                  AND is_disabled = 0 AND is_not_trusted = 0
                """).SingleAsync());
            Assert.Equal(2, await context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM sys.foreign_keys
                WHERE name IN ('FK_ProcessHistories_AspNetUsers_NewAssignedToUserId',
                               'FK_ProcessHistories_AspNetUsers_PreviousAssignedToUserId')
                  AND delete_referential_action_desc = 'NO_ACTION'
                """).SingleAsync());
            Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM sys.indexes i
                JOIN sys.tables t ON i.object_id = t.object_id
                WHERE t.name = 'InspectionSubmissions'
                  AND i.name = 'UX_InspectionSubmissions_InspectionId_RevisionNumber'
                  AND i.is_unique = 1
                """).SingleAsync());
        });
    }

    private async Task<Fixture> CreatePublishedFixtureAsync(string suffix, string? schema = null)
    {
        var constructionObject = await CreateConstructionObjectAsync(suffix);
        var template = await CreateTemplateAsync(suffix, publish: true, schema);
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

    private async Task<Fixture> CreateTemplateAsync(string suffix, bool publish, string? schema = null)
    {
        using var admin = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var response = await admin.PostAsJsonAsync("/api/form-templates", new
        {
            code = $"INS-{suffix}-{Guid.NewGuid():N}",
            name = $"Inspection template {suffix}",
            description = "Integration test",
            schemaJson = schema ?? Schema
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

    private async Task<Guid> GetUserIdAsync(string email)
    {
        var result = Guid.Empty;
        await factory.ExecuteDatabaseAsync(async context =>
        {
            result = await context.Users.Where(x => x.NormalizedEmail == email.ToUpperInvariant())
                .Select(x => x.Id).SingleAsync();
        });
        return result;
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

    private static object SubmitRequest(JsonElement detail, Guid reviewerUserId, string? staleToken = null) => new
    {
        reviewerUserId,
        inspectionRowVersion = staleToken == "inspection" ? "AA==" : RequiredString(detail, "rowVersion"),
        submissionRowVersion = staleToken == "submission"
            ? "AA=="
            : RequiredString(detail.GetProperty("currentRevision"), "rowVersion"),
        processRowVersion = staleToken == "process" ? "AA==" : RequiredString(detail, "processRowVersion")
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
