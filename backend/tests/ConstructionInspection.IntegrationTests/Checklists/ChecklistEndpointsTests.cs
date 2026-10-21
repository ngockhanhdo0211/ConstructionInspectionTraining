using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Infrastructure.Persistence;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.IntegrationTests.Checklists;

public sealed class ChecklistEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@local.test";
    private const string InspectorEmail = "inspector@local.test";
    private const string ReviewerEmail = "reviewer@local.test";
    private const string ManagerEmail = "manager@local.test";
    private const string InspectionSchema = """
        {"components":[{"type":"textfield","key":"summary","label":"Summary"}]}
        """;

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DatabaseContract_HasMigrationSequenceTablesConstraintsIndexesAndNoActionForeignKeys()
    {
        await factory.ExecuteDatabaseAsync(async context =>
        {
            var migrations = await context.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261020133408_AddChecklists", migrations);

            var schemaCounts = await context.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value] FROM sys.sequences
                WHERE [name] = 'ChecklistNumberSequence' AND [start_value] = 1 AND [increment] = 1
                UNION ALL
                SELECT COUNT(*) FROM sys.tables
                WHERE [name] IN ('Checklists','ChecklistItems','ChecklistInspections','ChecklistSubmissions',
                                 'ChecklistSubmissionItems','ChecklistSubmissionInspections')
                UNION ALL
                SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
                WHERE t.name = 'BusinessProcesses' AND i.is_unique = 1
                  AND i.name IN ('UX_BusinessProcesses_InspectionId','UX_BusinessProcesses_ChecklistId')
                  AND i.has_filter = 1
                UNION ALL
                SELECT COUNT(*) FROM sys.check_constraints
                WHERE [name] = 'CK_BusinessProcesses_Owner' AND is_disabled = 0 AND is_not_trusted = 0
                UNION ALL
                SELECT COUNT(*) FROM sys.foreign_keys
                WHERE (parent_object_id IN (
                           OBJECT_ID('Checklists'), OBJECT_ID('ChecklistItems'), OBJECT_ID('ChecklistInspections'),
                           OBJECT_ID('ChecklistSubmissions'), OBJECT_ID('ChecklistSubmissionItems'),
                           OBJECT_ID('ChecklistSubmissionInspections'))
                       OR [name] IN ('FK_BusinessProcesses_Checklists_ChecklistId',
                                     'FK_ProcessHistories_ChecklistSubmissions_ChecklistSubmissionId'))
                  AND delete_referential_action_desc <> 'NO_ACTION'
                UNION ALL
                SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
                WHERE (t.name = 'Checklists' AND c.name = 'RowVersion' AND TYPE_NAME(c.user_type_id) = 'timestamp')
                   OR (t.name = 'ChecklistItems' AND c.name = 'RowVersion' AND TYPE_NAME(c.user_type_id) = 'timestamp')
                """).ToArrayAsync();
            Assert.Equal([1, 6, 2, 1, 0, 2], schemaCounts);

            var actorId = await context.Users.Select(x => x.Id).FirstAsync();
            var bothOwnersNull = await Assert.ThrowsAsync<SqlException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [BusinessProcesses]
                        ([Id],[EntityType],[Status],[CreatedByUserId],[CreatedAt])
                    VALUES ({Guid.NewGuid()}, {"Checklist"}, {"Draft"}, {actorId}, {DateTime.UtcNow})
                    """));
            Assert.Equal(547, bothOwnersNull.Number);
        });
    }

    [Fact]
    public async Task CreateListAndDetail_EnforceAuthenticationRoleScopeAndActiveObject()
    {
        var activeObject = await CreateConstructionObjectAsync("ROLE-ACTIVE");
        var inactiveObject = await CreateConstructionObjectAsync("ROLE-INACTIVE");
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        Assert.Equal(HttpStatusCode.OK, (await administrator.PostAsJsonAsync(
            $"/api/construction-objects/{inactiveObject.Id}/deactivate",
            new { inactiveObject.RowVersion })).StatusCode);

        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var created = await CreateChecklistAsync(inspector, activeObject.Id, "CHK role scope",
            [new("Visual check", true)]);
        var id = created.GetProperty("checklistId").GetGuid();
        var detail = await GetDetailAsync(inspector, id);
        Assert.StartsWith("CHK-", detail.GetProperty("number").GetString(), StringComparison.Ordinal);
        Assert.Equal("Draft", detail.GetProperty("status").GetString());
        Assert.Single(detail.GetProperty("items").EnumerateArray());

        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/checklists")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/checklists/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(
            "/api/checklists", CreateRequest(activeObject.Id, "Anonymous", [new("Item", true)]))).StatusCode);

        var otherInspectorEmail = $"other-inspector-{Guid.NewGuid():N}@local.test";
        await factory.CreateUserAsync(otherInspectorEmail, "Other Inspector", ApplicationRoles.Inspector);
        using var otherInspector = await CreateAuthenticatedClientAsync(otherInspectorEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await otherInspector.GetAsync($"/api/checklists/{id}")).StatusCode);

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"/api/checklists/{id}")).StatusCode);

        foreach (var email in new[] { ManagerEmail, AdministratorEmail })
        {
            using var reader = await CreateAuthenticatedClientAsync(email);
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/checklists?search=role&pageSize=100")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/checklists/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(
                "/api/checklists", CreateRequest(activeObject.Id, "Forbidden", [new("Item", true)]))).StatusCode);
        }

        await AssertProblemAsync(await inspector.PostAsJsonAsync(
            "/api/checklists", CreateRequest(inactiveObject.Id, "Inactive", [new("Item", true)])),
            HttpStatusCode.Conflict, "checklist.object_inactive");
    }

    [Fact]
    public async Task ItemAuthoring_AllowsBoundaryReorderAndRejectsInvalidOrStaleChanges()
    {
        var constructionObject = await CreateConstructionObjectAsync("ITEMS");
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var initialItems = Enumerable.Range(1, 200)
            .Select(index => new CreateItem($"Criterion {index:000}", index % 2 == 0)).ToArray();
        var created = await CreateChecklistAsync(inspector, constructionObject.Id, "200 item boundary", initialItems);
        var id = created.GetProperty("checklistId").GetGuid();
        var detail = await GetDetailAsync(inspector, id);
        Assert.Equal(200, detail.GetProperty("items").GetArrayLength());

        var start = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/checklists/{id}/start", new
        {
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion")
        }));
        detail = await GetDetailAsync(inspector, id);

        var reordered = detail.GetProperty("items").EnumerateArray().Reverse()
            .Select(ItemRequest).ToArray();
        var reorderResponse = await inspector.PutAsJsonAsync($"/api/checklists/{id}/items", new
        {
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion"),
            items = reordered
        });
        Assert.Equal(HttpStatusCode.OK, reorderResponse.StatusCode);
        var reorderedResult = await ReadJsonAsync(reorderResponse);
        Assert.Equal("Criterion 200", reorderedResult.GetProperty("items")[0].GetProperty("criterion").GetString());

        var overCapacity = reorderedResult.GetProperty("items").EnumerateArray().Select(ItemRequest).Cast<object>()
            .Append(new { criterion = "Item 201", isRequired = true, result = "NotChecked", note = (string?)null })
            .ToArray();
        await AssertProblemAsync(await inspector.PutAsJsonAsync($"/api/checklists/{id}/items", new
        {
            checklistRowVersion = RequiredString(reorderedResult, "checklistRowVersion"),
            processRowVersion = RequiredString(reorderedResult, "processRowVersion"),
            items = overCapacity
        }), HttpStatusCode.BadRequest);

        var invalidFail = reorderedResult.GetProperty("items").EnumerateArray().Select(ItemRequest).ToArray();
        invalidFail[0] = new
        {
            id = reorderedResult.GetProperty("items")[0].GetProperty("id").GetGuid(),
            rowVersion = RequiredString(reorderedResult.GetProperty("items")[0], "rowVersion"),
            criterion = reorderedResult.GetProperty("items")[0].GetProperty("criterion").GetString(),
            isRequired = reorderedResult.GetProperty("items")[0].GetProperty("isRequired").GetBoolean(),
            result = "Fail",
            note = (string?)null
        };
        await AssertProblemAsync(await inspector.PutAsJsonAsync($"/api/checklists/{id}/items", new
        {
            checklistRowVersion = RequiredString(reorderedResult, "checklistRowVersion"),
            processRowVersion = RequiredString(reorderedResult, "processRowVersion"),
            items = invalidFail
        }), HttpStatusCode.BadRequest);

        await AssertProblemAsync(await inspector.PostAsJsonAsync($"/api/checklists/{id}/start", new
        {
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(start, "processRowVersion")
        }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PassFlow_SubmitsOnceAndAssignedReviewerApprovesImmutableSnapshot()
    {
        var constructionObject = await CreateConstructionObjectAsync("PASS");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var id = (await CreateChecklistAsync(inspector, constructionObject.Id, "Pass flow",
            [new("Foundation accepted", true), new("Access safe", false)]))
            .GetProperty("checklistId").GetGuid();
        var detail = await StartAsync(inspector, id);
        detail = await UpdateAllItemsAsync(inspector, detail, "Pass", null);

        var submitRequest = new
        {
            reviewerUserId = reviewerId,
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion")
        };
        var firstSubmit = inspector.PostAsJsonAsync($"/api/checklists/{id}/submit", submitRequest);
        var secondSubmit = inspector.PostAsJsonAsync($"/api/checklists/{id}/submit", submitRequest);
        var submitResponses = await Task.WhenAll(firstSubmit, secondSubmit);
        Assert.Single(submitResponses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(submitResponses, response => response.StatusCode == HttpStatusCode.Conflict);
        var submitted = await ReadJsonAsync(submitResponses.Single(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, submitted.GetProperty("submissionNumber").GetInt32());

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync($"/api/checklists/{id}")).StatusCode);
        var started = await ReadSuccessAsync(await reviewer.PostAsJsonAsync($"/api/checklists/{id}/start-review", new
        {
            processRowVersion = RequiredString(submitted, "processRowVersion")
        }));
        var approved = await ReadSuccessAsync(await reviewer.PostAsJsonAsync($"/api/checklists/{id}/approve", new
        {
            comment = "Accepted",
            processRowVersion = RequiredString(started, "processRowVersion")
        }));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        await AssertProblemAsync(await reviewer.PostAsJsonAsync($"/api/checklists/{id}/recheck", new
        {
            reason = "Too late",
            processRowVersion = RequiredString(approved, "processRowVersion")
        }), HttpStatusCode.Conflict);

        var submissions = await ReadSuccessAsync(await inspector.GetAsync($"/api/checklists/{id}/submissions"));
        Assert.Equal(1, submissions.GetProperty("totalCount").GetInt32());
        Assert.False(submissions.GetProperty("items")[0].TryGetProperty("items", out _));
        var snapshot = await ReadSuccessAsync(await inspector.GetAsync($"/api/checklists/{id}/submissions/1"));
        Assert.Equal(2, snapshot.GetProperty("items").GetArrayLength());
        Assert.All(snapshot.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("Pass", item.GetProperty("result").GetString()));
        var history = await ReadSuccessAsync(await inspector.GetAsync($"/api/checklists/{id}/history?pageSize=100"));
        Assert.Contains(history.GetProperty("items").EnumerateArray(), item => item.GetProperty("action").GetString() == "Approve");
    }

    [Fact]
    public async Task FailFlow_RequiresLinkThenRecheckResubmitPreservesBothSnapshotsAndInspectionProcess()
    {
        var constructionObject = await CreateConstructionObjectAsync("FAIL");
        var otherObject = await CreateConstructionObjectAsync("OTHER");
        var reviewerId = await GetUserIdAsync(ReviewerEmail);
        using var inspector = await CreateAuthenticatedClientAsync(InspectorEmail);
        var inspectionId = await CreateInspectionAsync(inspector, constructionObject.Id, "Linked inspection");
        var otherInspectionId = await CreateInspectionAsync(inspector, otherObject.Id, "Other inspection");
        var id = (await CreateChecklistAsync(inspector, constructionObject.Id, "Fail flow",
            [new("Concrete surface", true)])).GetProperty("checklistId").GetGuid();
        var detail = await StartAsync(inspector, id);
        detail = await UpdateAllItemsAsync(inspector, detail, "Fail", "Crack observed");

        await AssertProblemAsync(await inspector.PostAsJsonAsync($"/api/checklists/{id}/submit", new
        {
            reviewerUserId = reviewerId,
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion")
        }), HttpStatusCode.Conflict, "checklist.inspection_required");

        await AssertProblemAsync(await inspector.PutAsJsonAsync($"/api/checklists/{id}/inspections", new
        {
            inspectionIds = new[] { otherInspectionId },
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion")
        }), HttpStatusCode.NotFound);

        var linked = await ReadSuccessAsync(await inspector.PutAsJsonAsync($"/api/checklists/{id}/inspections", new
        {
            inspectionIds = new[] { inspectionId },
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion")
        }));
        var submitted = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/checklists/{id}/submit", new
        {
            reviewerUserId = reviewerId,
            checklistRowVersion = RequiredString(linked, "checklistRowVersion"),
            processRowVersion = RequiredString(linked, "processRowVersion")
        }));

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        var underReview = await ReadSuccessAsync(await reviewer.PostAsJsonAsync($"/api/checklists/{id}/start-review", new
        {
            processRowVersion = RequiredString(submitted, "processRowVersion")
        }));
        var recheck = await ReadSuccessAsync(await reviewer.PostAsJsonAsync($"/api/checklists/{id}/recheck", new
        {
            reason = "Repair and inspect again",
            processRowVersion = RequiredString(underReview, "processRowVersion")
        }));
        Assert.Equal("Recheck", recheck.GetProperty("status").GetString());

        var recheckDetail = await GetDetailAsync(inspector, id);
        await AssertProblemAsync(await inspector.PutAsJsonAsync($"/api/checklists/{id}/items", new
        {
            checklistRowVersion = RequiredString(recheckDetail, "checklistRowVersion"),
            processRowVersion = RequiredString(recheckDetail, "processRowVersion"),
            items = recheckDetail.GetProperty("items").EnumerateArray().Select(ItemRequest).ToArray()
        }), HttpStatusCode.Conflict);

        var resumed = await StartAsync(inspector, id);
        var passed = await UpdateAllItemsAsync(inspector, resumed, "Pass", "Repair verified");
        var resubmitted = await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/checklists/{id}/submit", new
        {
            reviewerUserId = reviewerId,
            checklistRowVersion = RequiredString(passed, "checklistRowVersion"),
            processRowVersion = RequiredString(passed, "processRowVersion")
        }));
        Assert.Equal(2, resubmitted.GetProperty("submissionNumber").GetInt32());

        var roundOne = await ReadSuccessAsync(await inspector.GetAsync($"/api/checklists/{id}/submissions/1"));
        var roundTwo = await ReadSuccessAsync(await inspector.GetAsync($"/api/checklists/{id}/submissions/2"));
        Assert.Equal("Fail", roundOne.GetProperty("items")[0].GetProperty("result").GetString());
        Assert.Equal("Pass", roundTwo.GetProperty("items")[0].GetProperty("result").GetString());
        Assert.Equal(inspectionId, roundOne.GetProperty("inspections")[0].GetProperty("id").GetGuid());

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var checklistProcess = await context.BusinessProcesses.AsNoTracking()
                .SingleAsync(x => x.ChecklistId == id);
            var inspectionProcess = await context.BusinessProcesses.AsNoTracking()
                .SingleAsync(x => x.InspectionId == inspectionId);
            Assert.Equal(ProcessEntityType.Checklist, checklistProcess.EntityType);
            Assert.Equal(ProcessEntityType.Inspection, inspectionProcess.EntityType);
            Assert.Null(checklistProcess.InspectionId);
            Assert.Null(inspectionProcess.ChecklistId);
        });
    }

    private async Task<(Guid Id, string RowVersion)> CreateConstructionObjectAsync(string suffix)
    {
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var response = await administrator.PostAsJsonAsync("/api/construction-objects", new
        {
            parentId = (Guid?)null,
            code = $"CHK-{suffix}-{Guid.NewGuid():N}",
            name = $"Checklist project {suffix}",
            objectType = "Project",
            location = "Integration test",
            description = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await ReadJsonAsync(response);
        return (payload.GetProperty("id").GetGuid(), RequiredString(payload, "rowVersion"));
    }

    private async Task<Guid> CreateInspectionAsync(HttpClient inspector, Guid constructionObjectId, string title)
    {
        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var templateResponse = await administrator.PostAsJsonAsync("/api/form-templates", new
        {
            code = $"CHK-INS-{Guid.NewGuid():N}",
            name = "Checklist inspection template",
            description = "Integration test",
            schemaJson = InspectionSchema
        });
        Assert.Equal(HttpStatusCode.Created, templateResponse.StatusCode);
        var createdTemplate = await ReadJsonAsync(templateResponse);
        var template = createdTemplate.GetProperty("template");
        var version = createdTemplate.GetProperty("version");
        var publishResponse = await administrator.PostAsJsonAsync(
            $"/api/form-templates/{template.GetProperty("id").GetGuid()}/versions/1/publish", new
            {
                templateRowVersion = RequiredString(template, "rowVersion"),
                versionRowVersion = RequiredString(version, "rowVersion")
            });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var inspectionResponse = await inspector.PostAsJsonAsync("/api/inspections", new
        {
            constructionObjectId,
            formTemplateId = template.GetProperty("id").GetGuid(),
            title,
            description = "Checklist integration link",
            data = new { summary = "Created for Checklist link" }
        });
        Assert.Equal(HttpStatusCode.Created, inspectionResponse.StatusCode);
        return (await ReadJsonAsync(inspectionResponse)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateChecklistAsync(
        HttpClient inspector, Guid constructionObjectId, string title, IReadOnlyCollection<CreateItem> items)
    {
        var response = await inspector.PostAsJsonAsync(
            "/api/checklists", CreateRequest(constructionObjectId, title, items));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created but received {(int)response.StatusCode}: {body}");
        Assert.NotNull(response.Headers.Location);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static object CreateRequest(
        Guid constructionObjectId, string title, IReadOnlyCollection<CreateItem> items) =>
        new
        {
            constructionObjectId,
            title,
            description = "Checklist integration test",
            items = items.Select(item => new { item.Criterion, item.IsRequired }).ToArray()
        };

    private async Task<JsonElement> StartAsync(HttpClient inspector, Guid id)
    {
        var detail = await GetDetailAsync(inspector, id);
        await ReadSuccessAsync(await inspector.PostAsJsonAsync($"/api/checklists/{id}/start", new
        {
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion")
        }));
        return await GetDetailAsync(inspector, id);
    }

    private static async Task<JsonElement> UpdateAllItemsAsync(
        HttpClient inspector, JsonElement detail, string result, string? note)
    {
        var id = detail.GetProperty("id").GetGuid();
        var items = detail.GetProperty("items").EnumerateArray().Select(item => new
        {
            id = item.GetProperty("id").GetGuid(),
            rowVersion = RequiredString(item, "rowVersion"),
            criterion = item.GetProperty("criterion").GetString(),
            isRequired = item.GetProperty("isRequired").GetBoolean(),
            result,
            note
        }).ToArray();
        await ReadSuccessAsync(await inspector.PutAsJsonAsync($"/api/checklists/{id}/items", new
        {
            checklistRowVersion = RequiredString(detail, "checklistRowVersion"),
            processRowVersion = RequiredString(detail, "processRowVersion"),
            items
        }));
        return await GetDetailAsync(inspector, id);
    }

    private static object ItemRequest(JsonElement item) => new
    {
        id = item.GetProperty("id").GetGuid(),
        rowVersion = RequiredString(item, "rowVersion"),
        criterion = item.GetProperty("criterion").GetString(),
        isRequired = item.GetProperty("isRequired").GetBoolean(),
        result = item.GetProperty("result").GetString(),
        note = item.GetProperty("note").ValueKind == JsonValueKind.Null ? null : item.GetProperty("note").GetString()
    };

    private static async Task<JsonElement> GetDetailAsync(HttpClient client, Guid id) =>
        await ReadSuccessAsync(await client.GetAsync($"/api/checklists/{id}"));

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

    private sealed record CreateItem(string Criterion, bool IsRequired);
}
