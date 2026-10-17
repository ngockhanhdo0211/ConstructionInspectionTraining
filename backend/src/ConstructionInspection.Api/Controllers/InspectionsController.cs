using System.Text.Json;
using System.Text.Json.Serialization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Commands;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Application.Inspections.Queries;
using ConstructionInspection.Domain.Inspections;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/inspections")]
public sealed class InspectionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<InspectionListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<InspectionListItemDto>> Get(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] InspectionStatus? status = null,
        [FromQuery] Guid? constructionObjectId = null,
        [FromQuery] Guid? reviewerUserId = null,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortDirection = "desc",
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetInspectionsQuery(
            pageNumber, pageSize, search, status, constructionObjectId,
            reviewerUserId, sortBy, sortDirection), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<InspectionDetailDto>(StatusCodes.Status200OK)]
    public Task<InspectionDetailDto> GetDetail(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetInspectionDetailQuery(id), cancellationToken);

    [HttpPost]
    [ProducesResponseType<InspectionDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InspectionDetailDto>> Create(
        CreateInspectionRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateInspectionCommand(
            request.ConstructionObjectId,
            request.FormTemplateId,
            request.Title,
            request.Description,
            JsonOrEmpty(request.Data)), cancellationToken);
        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}/draft")]
    [ProducesResponseType<InspectionDraftDto>(StatusCodes.Status200OK)]
    public Task<InspectionDraftDto> SaveDraft(
        Guid id, SaveInspectionDraftRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SaveInspectionDraftCommand(
            id,
            JsonOrEmpty(request.Data),
            request.InspectionRowVersion,
            request.SubmissionRowVersion,
            request.ProcessRowVersion), cancellationToken);

    private static string JsonOrEmpty(JsonElement value) =>
        value.ValueKind == JsonValueKind.Undefined ? string.Empty : value.GetRawText();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateInspectionRequest(
    Guid ConstructionObjectId,
    Guid FormTemplateId,
    string Title,
    string? Description,
    JsonElement Data);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveInspectionDraftRequest(
    JsonElement Data,
    string InspectionRowVersion,
    string SubmissionRowVersion,
    string ProcessRowVersion);
