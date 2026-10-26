using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ConstructionInspection.Api.Controllers;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Infrastructure.Persistence;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructionInspection.IntegrationTests.Rfis;

public sealed class RfiEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@local.test";
    private const string InspectorEmail = "inspector@local.test";
    private const string ReviewerEmail = "reviewer@local.test";
    private const string ManagerEmail = "manager@local.test";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DatabaseAndRouteContracts_HaveExpectedSchemaAndExactlyTwelveEndpoints()
    {
        var routes = typeof(RfisController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                .Select(attribute => $"{attribute.HttpMethods.Single()}:{attribute.Template}"))
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(12, routes.Length);
        Assert.Contains("GET:responder-options", routes);
        Assert.Contains("GET:{id:guid}", routes);

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var migrations = await context.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261026053735_AddRfis", migrations);

            var counts = await context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value] FROM sys.sequences
                WHERE [name] = 'RfiNumberSequence' AND [start_value] = 1 AND [increment] = 1
                UNION ALL
                SELECT COUNT(*) FROM sys.tables WHERE [name] IN ('Rfis','RfiEntries')
                UNION ALL
                SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
                WHERE t.name IN ('Rfis','BusinessProcesses') AND c.name = 'RowVersion'
                  AND TYPE_NAME(c.user_type_id) = 'timestamp' AND c.is_nullable = 0
                UNION ALL
                SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
                WHERE i.is_unique = 1 AND i.name IN
                  ('UX_Rfis_Number','UX_RfiEntries_RfiId_EntryNumber','UX_BusinessProcesses_RfiId')
                UNION ALL
                SELECT COUNT(*) FROM sys.check_constraints
                WHERE [name] IN ('CK_Rfis_NextEntryNumber','CK_RfiEntries_EntryNumber','CK_RfiEntries_EntryType',
                  'CK_BusinessProcesses_Owner','CK_BusinessProcesses_EntityType','CK_BusinessProcesses_Status',
                  'CK_ProcessHistories_Action','CK_ProcessHistories_ToStatus','CK_ProcessHistories_SubmissionOwner')
                  AND is_disabled = 0 AND is_not_trusted = 0
                UNION ALL
                SELECT COUNT(*) FROM sys.foreign_keys
                WHERE ([name] LIKE 'FK_Rfis_%' OR [name] LIKE 'FK_RfiEntries_%'
                    OR [name] IN ('FK_BusinessProcesses_Rfis_RfiId','FK_ProcessHistories_RfiEntries_RfiEntryId'))
                  AND delete_referential_action_desc <> 'NO_ACTION'
                """).ToArrayAsync();
            Assert.Equal([1, 2, 2, 3, 9, 0], counts);

            var actorId = await context.Users.Select(x => x.Id).FirstAsync();
            var invalidOwner = await Assert.ThrowsAsync<SqlException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [BusinessProcesses]
                        ([Id],[EntityType],[Status],[CreatedByUserId],[CreatedAt])
                    VALUES ({Guid.NewGuid()}, {"Rfi"}, {"Draft"}, {actorId}, {DateTime.UtcNow})
                    """));
            Assert.Equal(547, invalidOwner.Number);
        });
    }

    [Fact]
    public async Task CreateListDetailAndResponderOptions_EnforceRoleScopeAndActiveObject()
    {
        var activeObject = await CreateConstructionObjectAsync("SCOPE");
        var inactiveObject = await CreateConstructionObjectAsync("INACTIVE");
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        Assert.Equal(HttpStatusCode.OK, (await administrator.PostAsJsonAsync(
            $"/api/construction-objects/{inactiveObject.Id}/deactivate",
            new { inactiveObject.RowVersion })).StatusCode);

        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var created = await CreateRfiAsync(inspector, activeObject.Id, "Pump room query", "Confirm pipe routing.");
        var id = created.GetProperty("rfiId").GetGuid();
        Assert.StartsWith("RFI-", (await GetDetailAsync(inspector, id)).GetProperty("number").GetString(), StringComparison.Ordinal);

        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rfis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rfis/responder-options")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/rfis/{id}")).StatusCode);

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        using var manager = await CreateAuthenticatedClientAsync(ManagerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync("/api/rfis/responder-options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.GetAsync("/api/rfis/responder-options")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await inspector.GetAsync("/api/rfis/responder-options?search=Demo&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/rfis/responder-options?pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/rfis/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/rfis/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync($"/api/rfis/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/rfis",
            new { constructionObjectId = activeObject.Id, subject = "Wrong role", question = "No" })).StatusCode);

        var inactiveResponse = await inspector.PostAsJsonAsync("/api/rfis", new
        {
            constructionObjectId = inactiveObject.Id,
            subject = "Inactive object",
            question = "Should fail"
        });
        await AssertProblemAsync(inactiveResponse, HttpStatusCode.Conflict, "rfi.object_inactive");

        var otherEmail = $"rfi-inspector-{Guid.NewGuid():N}@local.test";
        await factory.CreateUserAsync(otherEmail, "Other Inspector", ApplicationRoles.Inspector);
        using var otherInspector = await CreateAuthenticatedClientAsync(otherEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await otherInspector.GetAsync($"/api/rfis/{id}")).StatusCode);

        var list = await ReadSuccessAsync(await inspector.GetAsync("/api/rfis?search=Pump&status=Draft&pageSize=10"));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("rfiRowVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("processRowVersion").GetString()));
        Assert.False(item.TryGetProperty("question", out _));
        Assert.False(item.TryGetProperty("entries", out _));
    }

    [Fact]
    public async Task CloseFlow_PreservesImmutableEntriesHistoryAndResetsDueAt()
    {
        var target = await CreateConstructionObjectAsync("CLOSE");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        var created = await CreateRfiAsync(inspector, target.Id, "Initial subject", "Initial question");
        var id = created.GetProperty("rfiId").GetGuid();

        var saved = await ReadSuccessAsync(await inspector.PutAsJsonAsync($"/api/rfis/{id}/draft", new
        {
            subject = "Updated subject",
            question = "Updated question",
            rfiRowVersion = RequiredString(created, "rfiRowVersion"),
            processRowVersion = RequiredString(created, "processRowVersion")
        }));
        await AssertProblemAsync(await inspector.PutAsJsonAsync($"/api/rfis/{id}/draft", new
        {
            subject = "Stale overwrite",
            question = "Must not win",
            rfiRowVersion = RequiredString(created, "rfiRowVersion"),
            processRowVersion = RequiredString(created, "processRowVersion")
        }), HttpStatusCode.Conflict, "rfi.concurrency_conflict");

        var beforeSubmit = DateTime.UtcNow;
        var submitted = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/submit", new
        {
            responderUserId = reviewerId,
            rfiRowVersion = RequiredString(saved, "rfiRowVersion"),
            processRowVersion = RequiredString(saved, "processRowVersion")
        }));
        Assert.Equal("Open", submitted.GetProperty("status").GetString());
        var firstDueAt = submitted.GetProperty("dueAtUtc").GetDateTime();
        Assert.InRange(firstDueAt, beforeSubmit.AddHours(71).AddMinutes(59), DateTime.UtcNow.AddHours(72).AddMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync($"/api/rfis/{id}")).StatusCode);

        var responded = await ReadSuccessAsync(await reviewer.PostAsJsonAsync($"/api/rfis/{id}/responses", new
        {
            responseText = "Use the revised route.",
            rfiRowVersion = RequiredString(submitted, "rfiRowVersion"),
            processRowVersion = RequiredString(submitted, "processRowVersion")
        }));
        Assert.Equal(1, responded.GetProperty("createdEntry").GetProperty("entryNumber").GetInt32());
        Assert.Equal("Response", responded.GetProperty("createdEntry").GetProperty("entryType").GetString());

        var clarified = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/request-clarification", new
        {
            content = "Confirm the revised elevation.",
            rfiRowVersion = RequiredString(responded, "rfiRowVersion"),
            processRowVersion = RequiredString(responded, "processRowVersion")
        }));
        Assert.Equal("Open", clarified.GetProperty("status").GetString());
        Assert.Equal(2, clarified.GetProperty("createdEntry").GetProperty("entryNumber").GetInt32());
        Assert.True(clarified.GetProperty("dueAtUtc").GetDateTime() >= firstDueAt);

        var respondedAgain = await ReadSuccessAsync(await reviewer.PostAsJsonAsync($"/api/rfis/{id}/responses", new
        {
            responseText = "Elevation confirmed.",
            rfiRowVersion = RequiredString(clarified, "rfiRowVersion"),
            processRowVersion = RequiredString(clarified, "processRowVersion")
        }));
        var closed = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/close", new
        {
            comment = "Accepted",
            rfiRowVersion = RequiredString(respondedAgain, "rfiRowVersion"),
            processRowVersion = RequiredString(respondedAgain, "processRowVersion")
        }));
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("completedAt").ValueKind);

        var detail = await GetDetailAsync(inspector, id);
        Assert.Equal([1, 2, 3], detail.GetProperty("entries").EnumerateArray()
            .Select(x => x.GetProperty("entryNumber").GetInt32()).ToArray());
        var history = await ReadSuccessAsync(await inspector.GetAsync($"/api/rfis/{id}/history?pageSize=100"));
        Assert.Equal(["Close", "Respond", "RequestClarification", "Respond", "Submit", "Create"],
            history.GetProperty("items").EnumerateArray().Select(x =>
                x.GetProperty("action").GetString() ?? throw new InvalidOperationException("History action is missing.")).ToArray());
        Assert.Equal(HttpStatusCode.Conflict, (await inspector.PostAsJsonAsync($"/api/rfis/{id}/cancel", new
        {
            reason = "Terminal",
            rfiRowVersion = RequiredString(closed, "rfiRowVersion"),
            processRowVersion = RequiredString(closed, "processRowVersion")
        })).StatusCode);
    }

    [Fact]
    public async Task ReassignAndCancelFlows_EnforceCurrentReviewerAndTerminalRules()
    {
        var target = await CreateConstructionObjectAsync("ASSIGN");
        var firstReviewerId = await GetUserIdAsync(ReviewerEmail);
        var secondEmail = $"rfi-reviewer-{Guid.NewGuid():N}@local.test";
        var secondReviewerId = await factory.CreateUserAsync(secondEmail, "Second Reviewer", ApplicationRoles.Reviewer);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var firstReviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        using var secondReviewer = await CreateAuthenticatedClientAsync(secondEmail);
        using var manager = await CreateAuthenticatedClientAsync(ManagerEmail);

        var created = await CreateRfiAsync(inspector, target.Id, "Reassign", "Who responds?");
        var id = created.GetProperty("rfiId").GetGuid();
        var submitted = await SubmitAsync(inspector, id, created, firstReviewerId);
        var reassigned = await ReadSuccessAsync(await manager.PostAsJsonAsync($"/api/rfis/{id}/assign", new
        {
            responderUserId = secondReviewerId,
            reason = "Discipline ownership changed",
            rfiRowVersion = RequiredString(submitted, "rfiRowVersion"),
            processRowVersion = RequiredString(submitted, "processRowVersion")
        }));
        Assert.Equal(secondReviewerId, reassigned.GetProperty("assignedToUserId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await firstReviewer.GetAsync($"/api/rfis/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondReviewer.GetAsync($"/api/rfis/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondReviewer.PostAsJsonAsync($"/api/rfis/{id}/assign", new
        {
            responderUserId = firstReviewerId,
            reason = "Wrong role",
            rfiRowVersion = RequiredString(reassigned, "rfiRowVersion"),
            processRowVersion = RequiredString(reassigned, "processRowVersion")
        })).StatusCode);

        var openCancelled = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/cancel", new
        {
            reason = "No longer required",
            rfiRowVersion = RequiredString(reassigned, "rfiRowVersion"),
            processRowVersion = RequiredString(reassigned, "processRowVersion")
        }));
        Assert.Equal("Cancelled", openCancelled.GetProperty("status").GetString());

        var draft = await CreateRfiAsync(inspector, target.Id, "Draft cancel", "Cancel before submit");
        var draftId = draft.GetProperty("rfiId").GetGuid();
        var draftCancelled = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/rfis/{draftId}/cancel", new
        {
            reason = "Duplicate",
            rfiRowVersion = RequiredString(draft, "rfiRowVersion"),
            processRowVersion = RequiredString(draft, "processRowVersion")
        }));
        Assert.Equal("Cancelled", draftCancelled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReviewerValidationAndPayloadBoundaries_FailClosedAndRoundTripPlainText()
    {
        var target = await CreateConstructionObjectAsync("VALIDATE");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        var managerId = await GetUserIdAsync(ManagerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var exactUtf8 = string.Concat(Enumerable.Repeat("á", 16_384));
        var created = await CreateRfiAsync(inspector, target.Id, "UTF-8 boundary", exactUtf8);
        var id = created.GetProperty("rfiId").GetGuid();
        Assert.Equal(exactUtf8, (await GetDetailAsync(inspector, id)).GetProperty("question").GetString());

        await AssertProblemAsync(await inspector.PostAsJsonAsync("/api/rfis", new
        {
            constructionObjectId = target.Id,
            subject = "Over byte limit",
            question = exactUtf8 + "á"
        }), HttpStatusCode.RequestEntityTooLarge, "rfi.payload_too_large");

        Assert.Equal(HttpStatusCode.BadRequest, (await inspector.PostAsJsonAsync("/api/rfis", new
        {
            constructionObjectId = target.Id,
            subject = "Unknown property",
            question = "Question",
            number = "CLIENT-OWNED"
        })).StatusCode);

        await AssertProblemAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/submit", new
        {
            responderUserId = managerId,
            rfiRowVersion = RequiredString(created, "rfiRowVersion"),
            processRowVersion = RequiredString(created, "processRowVersion")
        }), HttpStatusCode.Conflict, "rfi.reviewer_invalid");

        await factory.SetUserActiveAsync(ReviewerEmail, false);
        await AssertProblemAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/submit", new
        {
            responderUserId = reviewerId,
            rfiRowVersion = RequiredString(created, "rfiRowVersion"),
            processRowVersion = RequiredString(created, "processRowVersion")
        }), HttpStatusCode.Conflict, "rfi.reviewer_invalid");
        await factory.SetUserActiveAsync(ReviewerEmail, true);

        await factory.AddUserRoleAsync(InspectorEmail, ApplicationRoles.Reviewer);
        var inspectorId = await GetUserIdAsync(InspectorEmail);
        await AssertProblemAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/submit", new
        {
            responderUserId = inspectorId,
            rfiRowVersion = RequiredString(created, "rfiRowVersion"),
            processRowVersion = RequiredString(created, "processRowVersion")
        }), HttpStatusCode.Conflict, "rfi.reviewer_invalid");

        var script = "<script>alert('not executed')</script>";
        var htmlRfi = await CreateRfiAsync(inspector, target.Id, "Plain text", script);
        Assert.Equal(script, (await GetDetailAsync(inspector, htmlRfi.GetProperty("rfiId").GetGuid()))
            .GetProperty("question").GetString());
    }

    [Fact]
    public async Task ConcurrentSubmitAndRespond_AllowExactlyOneTransitionAndOneImmutableEntry()
    {
        var target = await CreateConstructionObjectAsync("RACE");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspectorOne = await CreateAuthenticatedClientAsync(InspectorEmail);
        using var inspectorTwo = await CreateAuthenticatedClientAsync(InspectorEmail);
        var created = await CreateRfiAsync(inspectorOne, target.Id, "Concurrent", "Submit once");
        var id = created.GetProperty("rfiId").GetGuid();
        var submitRequest = new
        {
            responderUserId = reviewerId,
            rfiRowVersion = RequiredString(created, "rfiRowVersion"),
            processRowVersion = RequiredString(created, "processRowVersion")
        };
        var submitResponses = await Task.WhenAll(
            inspectorOne.PostAsJsonAsync($"/api/rfis/{id}/submit", submitRequest),
            inspectorTwo.PostAsJsonAsync($"/api/rfis/{id}/submit", submitRequest));
        Assert.Equal(1, submitResponses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, submitResponses.Count(x => x.StatusCode == HttpStatusCode.Conflict));

        var open = await GetDetailAsync(inspectorOne, id);
        using var reviewerOne = await CreateAuthenticatedClientAsync(ReviewerEmail);
        using var reviewerTwo = await CreateAuthenticatedClientAsync(ReviewerEmail);
        var responseRequest = new
        {
            responseText = "Only one response",
            rfiRowVersion = RequiredString(open, "rfiRowVersion"),
            processRowVersion = RequiredString(open, "processRowVersion")
        };
        var responseResults = await Task.WhenAll(
            reviewerOne.PostAsJsonAsync($"/api/rfis/{id}/responses", responseRequest),
            reviewerTwo.PostAsJsonAsync($"/api/rfis/{id}/responses", responseRequest));
        Assert.Equal(1, responseResults.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responseResults.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Single((await GetDetailAsync(inspectorOne, id)).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task RfiFlow_DoesNotCallObjectStorageAndKeepsProcessOwnersIsolated()
    {
        var target = await CreateConstructionObjectAsync("ISOLATION");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var first = await CreateRfiAsync(inspector, target.Id, "Sequence one", "Question one");
        var second = await CreateRfiAsync(inspector, target.Id, "Sequence two", "Question two");
        var firstDetail = await GetDetailAsync(inspector, first.GetProperty("rfiId").GetGuid());
        var secondDetail = await GetDetailAsync(inspector, second.GetProperty("rfiId").GetGuid());
        Assert.NotEqual(firstDetail.GetProperty("number").GetString(), secondDetail.GetProperty("number").GetString());
        Assert.Equal(0, factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>().CallCount);

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var processes = await context.BusinessProcesses.AsNoTracking()
                .Where(x => x.RfiId != null).ToArrayAsync();
            Assert.Equal(2, processes.Length);
            Assert.All(processes, process =>
            {
                Assert.Equal(ProcessEntityType.Rfi, process.EntityType);
                Assert.Null(process.InspectionId);
                Assert.Null(process.ChecklistId);
                Assert.NotNull(process.RfiId);
            });
        });
    }

    private async Task<(Guid Id, string RowVersion)> CreateConstructionObjectAsync(string suffix)
    {
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var response = await administrator.PostAsJsonAsync("/api/construction-objects", new
        {
            parentId = (Guid?)null,
            code = $"RFI-{suffix}-{Guid.NewGuid():N}",
            name = $"RFI project {suffix}",
            objectType = "Project",
            location = "Integration test",
            description = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        return (payload.GetProperty("id").GetGuid(), RequiredString(payload, "rowVersion"));
    }

    private static async Task<JsonElement> CreateRfiAsync(
        HttpClient inspector, Guid constructionObjectId, string subject, string question)
    {
        var response = await inspector.PostAsJsonAsync("/api/rfis", new { constructionObjectId, subject, question });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created but received {(int)response.StatusCode}: {body}");
        Assert.NotNull(response.Headers.Location);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static async Task<JsonElement> SubmitAsync(
        HttpClient inspector, Guid id, JsonElement versions, Guid reviewerId) =>
        await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/rfis/{id}/submit", new
        {
            responderUserId = reviewerId,
            rfiRowVersion = RequiredString(versions, "rfiRowVersion"),
            processRowVersion = RequiredString(versions, "processRowVersion")
        }));

    private static async Task<JsonElement> GetDetailAsync(HttpClient client, Guid id) =>
        await ReadSuccessAsync(await client.GetAsync($"/api/rfis/{id}"));

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

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new InvalidOperationException($"Expected '{property}'.");

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Expected success but received {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

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
            Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
