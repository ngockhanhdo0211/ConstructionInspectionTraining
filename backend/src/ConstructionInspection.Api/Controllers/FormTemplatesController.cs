using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.FormTemplates.Commands;
using ConstructionInspection.Application.FormTemplates.Models;
using ConstructionInspection.Application.FormTemplates.Queries;
using ConstructionInspection.Domain.FormTemplates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("api/form-templates")]
public sealed class FormTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<FormTemplateListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<FormTemplateListItemDto>> Get(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] FormTemplateStatus? status = null,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDirection = "asc",
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetFormTemplatesQuery(
            pageNumber, pageSize, search, status, sortBy, sortDirection), cancellationToken);

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<FormTemplateVersionsDto>(StatusCodes.Status200OK)]
    public Task<FormTemplateVersionsDto> GetVersions(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "versionNumber",
        [FromQuery] string sortDirection = "desc",
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetFormTemplateVersionsQuery(
            id, pageNumber, pageSize, sortBy, sortDirection), cancellationToken);

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    [ProducesResponseType<FormTemplateVersionDetailDto>(StatusCodes.Status200OK)]
    public Task<FormTemplateVersionDetailDto> GetVersion(
        Guid id,
        int versionNumber,
        CancellationToken cancellationToken) =>
        sender.Send(new GetFormTemplateVersionDetailQuery(id, versionNumber), cancellationToken);

    [HttpPost]
    [ProducesResponseType<CreatedFormTemplateDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedFormTemplateDto>> Create(
        CreateFormTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateFormTemplateCommand(
            request.Code, request.Name, request.Description, request.SchemaJson), cancellationToken);
        return CreatedAtAction(
            nameof(GetVersion),
            new { id = result.Template.Id, versionNumber = result.Version.VersionNumber },
            result);
    }

    [HttpPut("{id:guid}/versions/{versionNumber:int}")]
    [ProducesResponseType<FormTemplateVersionDetailDto>(StatusCodes.Status200OK)]
    public Task<FormTemplateVersionDetailDto> UpdateDraft(
        Guid id,
        int versionNumber,
        UpdateFormTemplateDraftRequest request,
        CancellationToken cancellationToken) =>
        sender.Send(new UpdateFormTemplateDraftCommand(
            id, versionNumber, request.SchemaJson, request.RowVersion), cancellationToken);

    [HttpPost("{id:guid}/versions/{versionNumber:int}/publish")]
    [ProducesResponseType<FormTemplateVersionDetailDto>(StatusCodes.Status200OK)]
    public Task<FormTemplateVersionDetailDto> Publish(
        Guid id,
        int versionNumber,
        PublishFormTemplateVersionRequest request,
        CancellationToken cancellationToken) =>
        sender.Send(new PublishFormTemplateVersionCommand(
            id, versionNumber, request.TemplateRowVersion, request.VersionRowVersion), cancellationToken);

    [HttpPost("{id:guid}/versions")]
    [ProducesResponseType<FormTemplateVersionDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<FormTemplateVersionDetailDto>> CreateVersion(
        Guid id,
        CreateFormTemplateVersionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateFormTemplateVersionCommand(
            id, request.SchemaJson, request.TemplateRowVersion), cancellationToken);
        return CreatedAtAction(
            nameof(GetVersion),
            new { id, versionNumber = result.VersionNumber },
            result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<DeactivatedFormTemplateDto>(StatusCodes.Status200OK)]
    public Task<DeactivatedFormTemplateDto> Deactivate(
        Guid id,
        DeactivateFormTemplateRequest request,
        CancellationToken cancellationToken) =>
        sender.Send(new DeactivateFormTemplateCommand(id, request.RowVersion), cancellationToken);
}

public sealed record CreateFormTemplateRequest(
    string Code,
    string Name,
    string? Description,
    string SchemaJson);

public sealed record UpdateFormTemplateDraftRequest(string SchemaJson, string RowVersion);
public sealed record PublishFormTemplateVersionRequest(string TemplateRowVersion, string VersionRowVersion);
public sealed record CreateFormTemplateVersionRequest(string SchemaJson, string TemplateRowVersion);
public sealed record DeactivateFormTemplateRequest(string RowVersion);
