using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Application.FormTemplates.Models;
using ConstructionInspection.Domain.FormTemplates;
using MediatR;

namespace ConstructionInspection.Application.FormTemplates.Commands;

public sealed record CreateFormTemplateCommand(string Code, string Name, string? Description, string SchemaJson)
    : IRequest<CreatedFormTemplateDto>;
public sealed record UpdateFormTemplateDraftCommand(Guid Id, int VersionNumber, string SchemaJson, string RowVersion)
    : IRequest<FormTemplateVersionDetailDto>;
public sealed record PublishFormTemplateVersionCommand(Guid Id, int VersionNumber, string TemplateRowVersion, string VersionRowVersion)
    : IRequest<FormTemplateVersionDetailDto>;
public sealed record CreateFormTemplateVersionCommand(Guid Id, string SchemaJson, string TemplateRowVersion)
    : IRequest<FormTemplateVersionDetailDto>;
public sealed record DeactivateFormTemplateCommand(Guid Id, string RowVersion)
    : IRequest<DeactivatedFormTemplateDto>;

public sealed class CreateFormTemplateCommandHandler(
    ICurrentUser currentUser, IClock clock, IFormIoSchemaValidator schemaValidator,
    IFormTemplateRepository repository, IFormTemplateReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateFormTemplateCommand, CreatedFormTemplateDto>
{
    public async Task<CreatedFormTemplateDto> Handle(CreateFormTemplateCommand request, CancellationToken cancellationToken)
    {
        var actor = FormTemplateAuthorization.RequireAdministrator(currentUser);
        var schema = schemaValidator.Validate(request.SchemaJson);
        var id = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var code = FormTemplateRules.NormalizeCode(request.Code);
            if (await repository.CodeExistsAsync(code, ct))
                throw new ConflictException("form_template.code_duplicate", "A Form Template with this Code already exists.");
            var now = clock.UtcNow;
            var template = FormTemplate.Create(Guid.NewGuid(), code, request.Name, request.Description, actor, now);
            var version = FormTemplateVersion.CreateDraft(Guid.NewGuid(), template.Id, 1, schema.CanonicalJson, actor, now);
            repository.Add(template);
            repository.Add(version);
            await unitOfWork.SaveChangesAsync(ct);
            return template.Id;
        }, cancellationToken);
        var detail = await readService.GetVersionDetailAsync(id, 1, cancellationToken)
            ?? throw Missing("The created Form Template could not be read.");
        return new CreatedFormTemplateDto(detail.Template, detail);
    }

    private static ResourceNotFoundException Missing(string message) => new("form_template.not_found", message);
}

public sealed class UpdateFormTemplateDraftCommandHandler(
    ICurrentUser currentUser, IClock clock, IFormIoSchemaValidator schemaValidator,
    IFormTemplateRepository repository, IFormTemplateReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateFormTemplateDraftCommand, FormTemplateVersionDetailDto>
{
    public async Task<FormTemplateVersionDetailDto> Handle(UpdateFormTemplateDraftCommand request, CancellationToken cancellationToken)
    {
        var actor = FormTemplateAuthorization.RequireAdministrator(currentUser);
        var schema = schemaValidator.Validate(request.SchemaJson);
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var template = await repository.GetForUpdateAsync(request.Id, ct) ?? throw TemplateMissing();
            template.EnsureActive();
            var version = await repository.GetVersionForUpdateAsync(request.Id, request.VersionNumber, ct) ?? throw VersionMissing();
            FormTemplateRowVersion.EnsureMatches(version.RowVersion, request.RowVersion, nameof(request.RowVersion));
            version.UpdateDraft(schema.CanonicalJson, actor, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
        return await readService.GetVersionDetailAsync(request.Id, request.VersionNumber, cancellationToken) ?? throw VersionMissing();
    }

    private static ResourceNotFoundException TemplateMissing() => new("form_template.not_found", "The Form Template was not found.");
    private static ResourceNotFoundException VersionMissing() => new("form_template.version_not_found", "The Form Template version was not found.");
}

public sealed class PublishFormTemplateVersionCommandHandler(
    ICurrentUser currentUser, IClock clock, IFormIoSchemaValidator schemaValidator,
    IFormTemplateRepository repository, IFormTemplateReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<PublishFormTemplateVersionCommand, FormTemplateVersionDetailDto>
{
    public async Task<FormTemplateVersionDetailDto> Handle(PublishFormTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var actor = FormTemplateAuthorization.RequireAdministrator(currentUser);
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var template = await repository.GetForUpdateAsync(request.Id, ct) ?? throw TemplateMissing();
            FormTemplateRowVersion.EnsureMatches(template.RowVersion, request.TemplateRowVersion, nameof(request.TemplateRowVersion));
            template.EnsureActive();
            var version = await repository.GetVersionForUpdateAsync(request.Id, request.VersionNumber, ct) ?? throw VersionMissing();
            FormTemplateRowVersion.EnsureMatches(version.RowVersion, request.VersionRowVersion, nameof(request.VersionRowVersion));
            schemaValidator.Validate(version.SchemaJson);
            var now = clock.UtcNow;
            version.Publish(actor, now);
            template.MarkPublished(actor, now);
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
        return await readService.GetVersionDetailAsync(request.Id, request.VersionNumber, cancellationToken) ?? throw VersionMissing();
    }

    private static ResourceNotFoundException TemplateMissing() => new("form_template.not_found", "The Form Template was not found.");
    private static ResourceNotFoundException VersionMissing() => new("form_template.version_not_found", "The Form Template version was not found.");
}

public sealed class CreateFormTemplateVersionCommandHandler(
    ICurrentUser currentUser, IClock clock, IFormIoSchemaValidator schemaValidator,
    IFormTemplateRepository repository, IFormTemplateReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateFormTemplateVersionCommand, FormTemplateVersionDetailDto>
{
    public async Task<FormTemplateVersionDetailDto> Handle(CreateFormTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var actor = FormTemplateAuthorization.RequireAdministrator(currentUser);
        var schema = schemaValidator.Validate(request.SchemaJson);
        var versionNumber = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var template = await repository.GetForUpdateAsync(request.Id, ct)
                ?? throw new ResourceNotFoundException("form_template.not_found", "The Form Template was not found.");
            FormTemplateRowVersion.EnsureMatches(template.RowVersion, request.TemplateRowVersion, nameof(request.TemplateRowVersion));
            template.EnsureActive();
            if (await repository.HasDraftAsync(template.Id, ct))
                throw new ConflictException("form_template.draft_exists", "The Form Template already has a Draft version.");
            var now = clock.UtcNow;
            var allocated = template.AllocateNextVersion(actor, now);
            repository.Add(FormTemplateVersion.CreateDraft(Guid.NewGuid(), template.Id, allocated, schema.CanonicalJson, actor, now));
            await unitOfWork.SaveChangesAsync(ct);
            return allocated;
        }, cancellationToken);
        return await readService.GetVersionDetailAsync(request.Id, versionNumber, cancellationToken)
            ?? throw new ResourceNotFoundException("form_template.version_not_found", "The created Form Template version could not be read.");
    }
}

public sealed class DeactivateFormTemplateCommandHandler(
    ICurrentUser currentUser, IClock clock, IFormTemplateRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateFormTemplateCommand, DeactivatedFormTemplateDto>
{
    public async Task<DeactivatedFormTemplateDto> Handle(DeactivateFormTemplateCommand request, CancellationToken cancellationToken)
    {
        var actor = FormTemplateAuthorization.RequireAdministrator(currentUser);
        var template = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var entity = await repository.GetForUpdateAsync(request.Id, ct)
                ?? throw new ResourceNotFoundException("form_template.not_found", "The Form Template was not found.");
            FormTemplateRowVersion.EnsureMatches(entity.RowVersion, request.RowVersion, nameof(request.RowVersion));
            entity.Deactivate(actor, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return entity;
        }, cancellationToken);
        return new DeactivatedFormTemplateDto(
            template.Id, template.Status, template.UpdatedByUserId!.Value, template.UpdatedAt!.Value,
            template.DeactivatedByUserId!.Value, template.DeactivatedAt!.Value,
            Convert.ToBase64String(template.RowVersion));
    }
}
