using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructionInspection.IntegrationTests.FormTemplates;

public sealed class FormTemplateEndpointsTests(ConstructionObjectApiFactory factory)
    : IClassFixture<ConstructionObjectApiFactory>, IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@local.test";
    private const string InspectorEmail = "inspector@local.test";
    private const string ReviewerEmail = "reviewer@local.test";
    private const string ManagerEmail = "manager@local.test";
    private const string EmptySchema = "{\"components\":[]}";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => factory.ResetDatabaseAsync();

    [Fact]
    public async Task Endpoints_EnforceAdministratorRoleForAllManagementOperations()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/form-templates")).StatusCode);

        foreach (var email in new[] { ManagerEmail, InspectorEmail, ReviewerEmail })
        {
            using var client = factory.CreateApiClient();
            ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));

            var get = await client.GetAsync("/api/form-templates");
            var post = await client.PostAsJsonAsync(
                "/api/form-templates",
                CreateRequest($"ROLE-{Guid.NewGuid():N}", EmptySchema));

            await AssertProblemAsync(get, HttpStatusCode.Forbidden);
            await AssertProblemAsync(post, HttpStatusCode.Forbidden);
        }

        using var administrator = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var created = await CreateAsync(administrator, "ROLE-ADMIN", EmptySchema);
        Assert.Equal("Active", Template(created).GetProperty("status").GetString());
    }

    [Fact]
    public async Task EightEndpoints_ImplementVersionLifecycleAndPreserveHistory()
    {
        using var client = await CreateAuthenticatedClientAsync(AdministratorEmail);
        const string initialSchema = """
            {"components":[{"type":"textfield","key":"title","label":"Title"}]}
            """;
        const string updatedSchema = """
            {"components":[{"type":"checkbox","key":"approved","label":"Approved","defaultValue":false}]}
            """;

        var created = await CreateAsync(client, "LIFE-01", initialSchema);
        var templateId = Template(created).GetProperty("id").GetGuid();
        var initialTemplateRowVersion = RowVersion(Template(created));
        var initialVersionRowVersion = RowVersion(Version(created));
        Assert.Equal(1, Version(created).GetProperty("versionNumber").GetInt32());
        Assert.Equal("Draft", Version(created).GetProperty("status").GetString());
        Assert.Equal(2, Template(created).GetProperty("nextVersionNumber").GetInt32());

        var duplicateCode = await client.PostAsJsonAsync(
            "/api/form-templates",
            CreateRequest("life-01", EmptySchema));
        await AssertConflictCodeAsync(duplicateCode, "form_template.code_duplicate");

        var listResponse = await client.GetAsync("/api/form-templates?pageNumber=1&pageSize=10&sortBy=code&sortDirection=asc");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadJsonAsync(listResponse);
        var listItem = list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == templateId);
        Assert.False(listItem.TryGetProperty("schemaJson", out _));
        Assert.Equal(1, listItem.GetProperty("draftVersionNumber").GetInt32());

        var versionsResponse = await client.GetAsync($"/api/form-templates/{templateId}/versions?pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versions = await ReadJsonAsync(versionsResponse);
        Assert.False(versions.GetProperty("versions").GetProperty("items")[0].TryGetProperty("schemaJson", out _));

        var detailResponse = await client.GetAsync($"/api/form-templates/{templateId}/versions/1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadJsonAsync(detailResponse);
        Assert.Equal(Canonicalize(initialSchema), detail.GetProperty("schemaJson").GetString());

        var updatedResponse = await client.PutAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/1",
            new { schemaJson = updatedSchema, rowVersion = initialVersionRowVersion });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await ReadJsonAsync(updatedResponse);
        Assert.Equal(updatedSchema, updated.GetProperty("schemaJson").GetString());
        Assert.True(updated.TryGetProperty("updatedByUserId", out var updatedBy) && updatedBy.ValueKind == JsonValueKind.String);

        var staleUpdate = await client.PutAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/1",
            new { schemaJson = initialSchema, rowVersion = initialVersionRowVersion });
        await AssertConflictCodeAsync(staleUpdate, "form_template.concurrency_conflict");

        var publishedResponse = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/1/publish",
            new
            {
                templateRowVersion = initialTemplateRowVersion,
                versionRowVersion = RowVersion(updated)
            });
        Assert.Equal(HttpStatusCode.OK, publishedResponse.StatusCode);
        var published = await ReadJsonAsync(publishedResponse);
        Assert.Equal("Published", published.GetProperty("status").GetString());
        Assert.True(published.TryGetProperty("publishedByUserId", out var publishedBy) && publishedBy.ValueKind == JsonValueKind.String);

        var publishAgain = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/1/publish",
            new
            {
                templateRowVersion = RowVersion(Template(published)),
                versionRowVersion = RowVersion(published)
            });
        await AssertConflictCodeAsync(publishAgain, "form_template.invalid_transition");

        var updatePublished = await client.PutAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/1",
            new { schemaJson = initialSchema, rowVersion = RowVersion(published) });
        await AssertConflictCodeAsync(updatePublished, "form_template.version_published");

        var secondVersionResponse = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions",
            new { schemaJson = EmptySchema, templateRowVersion = RowVersion(Template(published)) });
        Assert.Equal(HttpStatusCode.Created, secondVersionResponse.StatusCode);
        var secondVersion = await ReadJsonAsync(secondVersionResponse);
        Assert.Equal(2, secondVersion.GetProperty("versionNumber").GetInt32());
        Assert.Equal("Draft", secondVersion.GetProperty("status").GetString());
        Assert.Equal(3, Template(secondVersion).GetProperty("nextVersionNumber").GetInt32());

        var duplicateDraft = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions",
            new { schemaJson = EmptySchema, templateRowVersion = RowVersion(Template(secondVersion)) });
        await AssertConflictCodeAsync(duplicateDraft, "form_template.draft_exists");

        var deactivatedResponse = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/deactivate",
            new { rowVersion = RowVersion(Template(secondVersion)) });
        Assert.Equal(HttpStatusCode.OK, deactivatedResponse.StatusCode);
        var deactivated = await ReadJsonAsync(deactivatedResponse);
        Assert.Equal("Inactive", deactivated.GetProperty("status").GetString());
        Assert.True(deactivated.TryGetProperty("deactivatedByUserId", out _));

        var historyResponse = await client.GetAsync($"/api/form-templates/{templateId}/versions?pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await ReadJsonAsync(historyResponse);
        Assert.Equal(2, history.GetProperty("versions").GetProperty("totalCount").GetInt32());

        var updateInactive = await client.PutAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/2",
            new { schemaJson = updatedSchema, rowVersion = RowVersion(secondVersion) });
        await AssertConflictCodeAsync(updateInactive, "form_template.template_inactive");

        var createOnInactive = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions",
            new { schemaJson = EmptySchema, templateRowVersion = RowVersion(deactivated) });
        await AssertConflictCodeAsync(createOnInactive, "form_template.template_inactive");

        var publishOnInactive = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/2/publish",
            new
            {
                templateRowVersion = RowVersion(deactivated),
                versionRowVersion = RowVersion(secondVersion)
            });
        await AssertConflictCodeAsync(publishOnInactive, "form_template.template_inactive");

        var deactivateAgain = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/deactivate",
            new { rowVersion = RowVersion(deactivated) });
        await AssertConflictCodeAsync(deactivateAgain, "form_template.already_inactive");

        Assert.Contains(
            (await client.DeleteAsync($"/api/form-templates/{templateId}")).StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/form-templates/{templateId}/reactivate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/form-templates/{Guid.NewGuid()}/versions")).StatusCode);
        Assert.Equal(0, factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>().CallCount);
    }

    [Fact]
    public async Task Validation_IsFailClosedAndUsesCountDepthAndUtf8Limits()
    {
        using var client = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var invalidSchemas = new[]
        {
            "{bad",
            "{\"components\":[{\"type\":\"file\",\"key\":\"upload\",\"label\":\"Upload\"}]}",
            "{\"components\":[{\"type\":\"textfield\",\"key\":\"a\",\"label\":\"A\",\"unknown\":true}]}",
            BuildComponentSchema(201),
            BuildTooDeepSchema()
        };

        for (var index = 0; index < invalidSchemas.Length; index++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/form-templates",
                CreateRequest($"INVALID-{index:00}", invalidSchemas[index]));
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }

        var exactSize = EmptySchema + new string(' ', 1_048_576 - Encoding.UTF8.GetByteCount(EmptySchema));
        Assert.Equal(1_048_576, Encoding.UTF8.GetByteCount(exactSize));
        var exactResponse = await client.PostAsJsonAsync(
            "/api/form-templates",
            CreateRequest("SIZE-EXACT", exactSize));
        Assert.Equal(HttpStatusCode.Created, exactResponse.StatusCode);

        var oversizedResponse = await client.PostAsJsonAsync(
            "/api/form-templates",
            CreateRequest("SIZE-OVER", exactSize + " "));
        await AssertProblemAsync(oversizedResponse, HttpStatusCode.BadRequest, "form_template.schema_too_large");

        var emptyResponse = await client.PostAsJsonAsync(
            "/api/form-templates",
            CreateRequest("EMPTY-SCHEMA", string.Empty));
        await AssertProblemAsync(emptyResponse, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SqlConstraintsAndConcurrentCreation_ProtectCountersAndDraftUniqueness()
    {
        using var client = await CreateAuthenticatedClientAsync(AdministratorEmail);
        var created = await CreateAsync(client, "CONCURRENT-01", EmptySchema);
        var templateId = Template(created).GetProperty("id").GetGuid();
        var actorId = Version(created).GetProperty("createdByUserId").GetGuid();

        var publishedResponse = await client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions/1/publish",
            new
            {
                templateRowVersion = RowVersion(Template(created)),
                versionRowVersion = RowVersion(Version(created))
            });
        Assert.Equal(HttpStatusCode.OK, publishedResponse.StatusCode);
        var published = await ReadJsonAsync(publishedResponse);
        var sharedTemplateRowVersion = RowVersion(Template(published));

        var first = client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions",
            new { schemaJson = EmptySchema, templateRowVersion = sharedTemplateRowVersion });
        var second = client.PostAsJsonAsync(
            $"/api/form-templates/{templateId}/versions",
            new { schemaJson = EmptySchema, templateRowVersion = sharedTemplateRowVersion });
        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        await factory.ExecuteDatabaseAsync(async context =>
        {
            var template = await context.FormTemplates.AsNoTracking().SingleAsync(item => item.Id == templateId);
            var versions = await context.FormTemplateVersions.AsNoTracking()
                .Where(item => item.FormTemplateId == templateId)
                .OrderBy(item => item.VersionNumber)
                .ToArrayAsync();
            Assert.Equal(3, template.NextVersionNumber);
            Assert.Equal(new[] { 1, 2 }, versions.Select(item => item.VersionNumber));
            Assert.Single(versions, item => item.Status == FormTemplateVersionStatus.Draft);

            var duplicateCode = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplates] ([Id],[Code],[Name],[Status],[NextVersionNumber],[CreatedByUserId],[CreatedAt]) VALUES ({Guid.NewGuid()}, {"CONCURRENT-01"}, {"Duplicate"}, {"Active"}, {2}, {actorId}, {DateTime.UtcNow})"));
            Assert.Contains(duplicateCode.Number, new[] { 2601, 2627 });

            var duplicateVersion = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplateVersions] ([Id],[FormTemplateId],[VersionNumber],[Status],[SchemaJson],[CreatedByUserId],[CreatedAt],[PublishedByUserId],[PublishedAt]) VALUES ({Guid.NewGuid()}, {templateId}, {1}, {"Published"}, {EmptySchema}, {actorId}, {DateTime.UtcNow}, {actorId}, {DateTime.UtcNow})"));
            Assert.Contains(duplicateVersion.Number, new[] { 2601, 2627 });

            var secondDraft = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplateVersions] ([Id],[FormTemplateId],[VersionNumber],[Status],[SchemaJson],[CreatedByUserId],[CreatedAt]) VALUES ({Guid.NewGuid()}, {templateId}, {99}, {"Draft"}, {EmptySchema}, {actorId}, {DateTime.UtcNow})"));
            Assert.Contains(secondDraft.Number, new[] { 2601, 2627 });

            var invalidJson = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplateVersions] ([Id],[FormTemplateId],[VersionNumber],[Status],[SchemaJson],[CreatedByUserId],[CreatedAt],[PublishedByUserId],[PublishedAt]) VALUES ({Guid.NewGuid()}, {templateId}, {100}, {"Published"}, {"{bad"}, {actorId}, {DateTime.UtcNow}, {actorId}, {DateTime.UtcNow})"));
            Assert.Equal(547, invalidJson.Number);

            var invalidTemplateStatus = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplates] ([Id],[Code],[Name],[Status],[NextVersionNumber],[CreatedByUserId],[CreatedAt]) VALUES ({Guid.NewGuid()}, {"INVALID-STATUS"}, {"Invalid"}, {"Archived"}, {2}, {actorId}, {DateTime.UtcNow})"));
            Assert.Equal(547, invalidTemplateStatus.Number);

            var invalidVersionStatus = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplateVersions] ([Id],[FormTemplateId],[VersionNumber],[Status],[SchemaJson],[CreatedByUserId],[CreatedAt]) VALUES ({Guid.NewGuid()}, {templateId}, {101}, {"Archived"}, {EmptySchema}, {actorId}, {DateTime.UtcNow})"));
            Assert.Equal(547, invalidVersionStatus.Number);

            var invalidVersionNumber = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [FormTemplateVersions] ([Id],[FormTemplateId],[VersionNumber],[Status],[SchemaJson],[CreatedByUserId],[CreatedAt],[PublishedByUserId],[PublishedAt]) VALUES ({Guid.NewGuid()}, {templateId}, {0}, {"Published"}, {EmptySchema}, {actorId}, {DateTime.UtcNow}, {actorId}, {DateTime.UtcNow})"));
            Assert.Equal(547, invalidVersionNumber.Number);

            Assert.All(versions, version => Assert.Equal(actorId, version.CreatedByUserId));
        });

        Assert.Equal(0, factory.Services.GetRequiredService<ConstructionObjectApiFactory.FakeObjectStorage>().CallCount);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        return client;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string code, string schema)
    {
        var response = await client.PostAsJsonAsync("/api/form-templates", CreateRequest(code, schema));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await ReadJsonAsync(response);
    }

    private static object CreateRequest(string code, string schemaJson) => new
    {
        code,
        name = $"Template {code}",
        description = "Form Template integration test",
        schemaJson
    };

    private static JsonElement Template(JsonElement payload) => payload.GetProperty("template");

    private static JsonElement Version(JsonElement payload) => payload.GetProperty("version");

    private static string RowVersion(JsonElement payload) =>
        payload.GetProperty("rowVersion").GetString()
        ?? throw new InvalidOperationException("A rowVersion was expected.");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertConflictCodeAsync(HttpResponseMessage response, string code) =>
        await AssertProblemAsync(response, HttpStatusCode.Conflict, code);

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string? expectedCode = null)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        if (expectedCode is not null)
            Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    private static string BuildComponentSchema(int count)
    {
        var components = Enumerable.Range(1, count)
            .Select(index => $"{{\"type\":\"checkbox\",\"key\":\"item{index}\",\"label\":\"Item {index}\"}}");
        return $"{{\"components\":[{string.Join(',', components)}]}}";
    }

    private static string Canonicalize(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string BuildTooDeepSchema()
    {
        var nested = "true";
        for (var depth = 0; depth < 20; depth++) nested = $"{{\"level\":{nested}}}";
        return $"{{\"components\":[],\"hidden\":{nested}}}";
    }
}
