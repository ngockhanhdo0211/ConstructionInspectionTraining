using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.FormTemplates.Commands;
using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Application.FormTemplates.Models;
using ConstructionInspection.Application.FormTemplates.Validation;
using ConstructionInspection.Domain.FormTemplates;

namespace ConstructionInspection.Application.UnitTests.FormTemplates;

public sealed class FormTemplateCommandHandlerTests
{
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
    private const string Schema = "{\"components\":[]}";

    [Fact]
    public async Task Create_AddsActiveTemplateAndDraftVersionOne()
    {
        var repository = new FakeRepository();

        await CreateHandler(repository).Handle(
            new CreateFormTemplateCommand(" form-01 ", "Form 01", null, Schema),
            CancellationToken.None);

        Assert.NotNull(repository.AddedTemplate);
        Assert.NotNull(repository.AddedVersion);
        Assert.Equal("FORM-01", repository.AddedTemplate.Code);
        Assert.Equal(2, repository.AddedTemplate.NextVersionNumber);
        Assert.Equal(1, repository.AddedVersion.VersionNumber);
        Assert.Equal(FormTemplateVersionStatus.Draft, repository.AddedVersion.Status);
        Assert.Equal(repository.AddedTemplate.Id, repository.AddedVersion.FormTemplateId);
    }

    [Fact]
    public async Task Create_WhenCodeExists_ReturnsConflict()
    {
        var repository = new FakeRepository { CodeExists = true };

        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(repository).Handle(
            new CreateFormTemplateCommand("FORM-01", "Form 01", null, Schema),
            CancellationToken.None));

        Assert.Equal("form_template.code_duplicate", error.Code);
    }

    [Fact]
    public async Task CreateVersion_AllocatesFromCounterAndAddsOneDraft()
    {
        var template = CreateTemplate();
        var repository = new FakeRepository { Template = template };
        var handler = new CreateFormTemplateVersionCommandHandler(
            Admin(), new FakeClock(), new FormIoSchemaValidator(), repository,
            new FakeReadService(repository), new FakeUnitOfWork());

        var result = await handler.Handle(
            new CreateFormTemplateVersionCommand(template.Id, Schema, string.Empty),
            CancellationToken.None);

        Assert.Equal(2, result.VersionNumber);
        Assert.Equal(3, template.NextVersionNumber);
        Assert.Equal(2, repository.AddedVersion!.VersionNumber);
    }

    [Fact]
    public async Task CreateVersion_WhenDraftExists_ReturnsConflictWithoutAllocatingNumber()
    {
        var template = CreateTemplate();
        var repository = new FakeRepository { Template = template, HasDraft = true };
        var handler = new CreateFormTemplateVersionCommandHandler(
            Admin(), new FakeClock(), new FormIoSchemaValidator(), repository,
            new FakeReadService(repository), new FakeUnitOfWork());

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateFormTemplateVersionCommand(template.Id, Schema, string.Empty),
            CancellationToken.None));

        Assert.Equal("form_template.draft_exists", error.Code);
        Assert.Equal(2, template.NextVersionNumber);
    }

    [Fact]
    public async Task Publish_ChangesOnlyDraftAndWritesBothAudits()
    {
        var template = CreateTemplate();
        var version = FormTemplateVersion.CreateDraft(Guid.NewGuid(), template.Id, 1, Schema, AdminId, Now);
        var repository = new FakeRepository { Template = template, Version = version };
        var handler = new PublishFormTemplateVersionCommandHandler(
            Admin(), new FakeClock(), new FormIoSchemaValidator(), repository,
            new FakeReadService(repository), new FakeUnitOfWork());

        await handler.Handle(
            new PublishFormTemplateVersionCommand(template.Id, 1, string.Empty, string.Empty),
            CancellationToken.None);

        Assert.Equal(FormTemplateVersionStatus.Published, version.Status);
        Assert.Equal(AdminId, version.PublishedByUserId);
        Assert.Equal(AdminId, template.UpdatedByUserId);
    }

    [Fact]
    public async Task Create_WithNonAdministrator_ReturnsForbidden()
    {
        var handler = CreateHandler(
            new FakeRepository(),
            new FakeCurrentUser(AdminId, [ApplicationRoles.Manager]));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(
            new CreateFormTemplateCommand("FORM-01", "Form 01", null, Schema),
            CancellationToken.None));
    }

    private static CreateFormTemplateCommandHandler CreateHandler(
        FakeRepository repository,
        ICurrentUser? currentUser = null) => new(
            currentUser ?? Admin(), new FakeClock(), new FormIoSchemaValidator(), repository,
            new FakeReadService(repository), new FakeUnitOfWork());

    private static FormTemplate CreateTemplate() =>
        FormTemplate.Create(Guid.NewGuid(), "FORM-01", "Form 01", null, AdminId, Now);

    private static FakeCurrentUser Admin() => new(AdminId, [ApplicationRoles.Administrator]);

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class FakeCurrentUser(Guid? userId, IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public IReadOnlyCollection<string> Roles { get; } = roles;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    private sealed class FakeRepository : IFormTemplateRepository
    {
        public FormTemplate? Template { get; init; }
        public FormTemplateVersion? Version { get; init; }
        public FormTemplate? AddedTemplate { get; private set; }
        public FormTemplateVersion? AddedVersion { get; private set; }
        public bool CodeExists { get; init; }
        public bool HasDraft { get; init; }

        public Task<FormTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult((Template ?? AddedTemplate) is { } value && value.Id == id ? value : null);
        public Task<FormTemplateVersion?> GetVersionForUpdateAsync(Guid templateId, int versionNumber, CancellationToken cancellationToken) =>
            Task.FromResult((Version ?? AddedVersion) is { } value
                && value.FormTemplateId == templateId && value.VersionNumber == versionNumber ? value : null);
        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(CodeExists);
        public Task<bool> HasDraftAsync(Guid templateId, CancellationToken cancellationToken) => Task.FromResult(HasDraft);
        public void Add(FormTemplate template) => AddedTemplate = template;
        public void Add(FormTemplateVersion version) => AddedVersion = version;
    }

    private sealed class FakeReadService(FakeRepository repository) : IFormTemplateReadService
    {
        public Task<PagedResult<FormTemplateListItemDto>> GetTemplatesAsync(
            FormTemplateQueryCriteria criteria,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FormTemplateVersionsDto?> GetVersionsAsync(
            Guid templateId,
            FormTemplateVersionQueryCriteria criteria,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FormTemplateVersionDetailDto?> GetVersionDetailAsync(
            Guid templateId,
            int versionNumber,
            CancellationToken cancellationToken)
        {
            var template = repository.Template ?? repository.AddedTemplate;
            var version = repository.Version ?? repository.AddedVersion;
            if (template is null || version is null) return Task.FromResult<FormTemplateVersionDetailDto?>(null);
            var header = new FormTemplateHeaderDto(
                template.Id, template.Code, template.Name, template.Description, template.Status,
                template.NextVersionNumber, Convert.ToBase64String(template.RowVersion));
            return Task.FromResult<FormTemplateVersionDetailDto?>(new(
                header, version.Id, version.VersionNumber, version.Status, version.SchemaJson,
                System.Text.Encoding.UTF8.GetByteCount(version.SchemaJson),
                version.CreatedByUserId, version.CreatedAt, version.UpdatedByUserId, version.UpdatedAt,
                version.PublishedByUserId, version.PublishedAt, Convert.ToBase64String(version.RowVersion)));
        }
    }
}
