using System.Text.Json;
using System.Text.Json.Serialization;
using ConstructionInspection.Application.Common.Authorization;
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

    [HttpGet("form-template-options")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [ProducesResponseType<PagedResult<FormTemplateOptionDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<FormTemplateOptionDto>> GetFormTemplateOptions(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetFormTemplateOptionsQuery(pageNumber, pageSize, search), cancellationToken);

    [HttpGet("reviewer-options")]
    [Authorize(Roles = ApplicationRoles.Inspector + "," + ApplicationRoles.Manager + "," + ApplicationRoles.Administrator)]
    [ProducesResponseType<PagedResult<ReviewerOptionDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ReviewerOptionDto>> GetReviewerOptions(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetReviewerOptionsQuery(pageNumber, pageSize, search), cancellationToken);

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

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [ProducesResponseType<InspectionWorkflowResultDto>(StatusCodes.Status200OK)]
    public Task<InspectionWorkflowResultDto> Submit(
        Guid id, SubmitInspectionRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SubmitInspectionCommand(
            id, request.ReviewerUserId, request.InspectionRowVersion,
            request.SubmissionRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/start-review")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    [ProducesResponseType<InspectionWorkflowResultDto>(StatusCodes.Status200OK)]
    public Task<InspectionWorkflowResultDto> StartReview(
        Guid id, StartInspectionReviewRequest request, CancellationToken cancellationToken) =>
        sender.Send(new StartInspectionReviewCommand(id, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    [ProducesResponseType<InspectionWorkflowResultDto>(StatusCodes.Status200OK)]
    public Task<InspectionWorkflowResultDto> Approve(
        Guid id, ApproveInspectionRequest request, CancellationToken cancellationToken) =>
        sender.Send(new ApproveInspectionCommand(id, request.Comment, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    [ProducesResponseType<InspectionWorkflowResultDto>(StatusCodes.Status200OK)]
    public Task<InspectionWorkflowResultDto> Reject(
        Guid id, RejectInspectionRequest request, CancellationToken cancellationToken) =>
        sender.Send(new RejectInspectionCommand(id, request.Reason, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/recheck")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    [ProducesResponseType<InspectionWorkflowResultDto>(StatusCodes.Status200OK)]
    public Task<InspectionWorkflowResultDto> RequestRecheck(
        Guid id, RequestInspectionRecheckRequest request, CancellationToken cancellationToken) =>
        sender.Send(new RequestInspectionRecheckCommand(id, request.Reason, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/reassign-reviewer")]
    [Authorize(Roles = ApplicationRoles.Manager + "," + ApplicationRoles.Administrator)]
    [ProducesResponseType<InspectionWorkflowResultDto>(StatusCodes.Status200OK)]
    public Task<InspectionWorkflowResultDto> ReassignReviewer(
        Guid id, ReassignInspectionReviewerRequest request, CancellationToken cancellationToken) =>
        sender.Send(new ReassignInspectionReviewerCommand(
            id, request.ReviewerUserId, request.Reason,
            request.InspectionRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<PagedResult<ProcessHistoryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ProcessHistoryDto>> GetHistory(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetInspectionHistoryQuery(id, pageNumber, pageSize), cancellationToken);

    [HttpGet("{id:guid}/revisions")]
    [ProducesResponseType<PagedResult<InspectionRevisionListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<InspectionRevisionListItemDto>> GetRevisions(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetInspectionRevisionsQuery(id, pageNumber, pageSize), cancellationToken);

    [HttpGet("{id:guid}/revisions/{revisionNumber:int}")]
    [ProducesResponseType<InspectionRevisionDetailDto>(StatusCodes.Status200OK)]
    public Task<InspectionRevisionDetailDto> GetRevisionDetail(
        Guid id, int revisionNumber, CancellationToken cancellationToken) =>
        sender.Send(new GetInspectionRevisionDetailQuery(id, revisionNumber), cancellationToken);

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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitInspectionRequest(
    Guid ReviewerUserId,
    string InspectionRowVersion,
    string SubmissionRowVersion,
    string ProcessRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartInspectionReviewRequest(string ProcessRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveInspectionRequest(string? Comment, string ProcessRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectInspectionRequest(string Reason, string ProcessRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestInspectionRecheckRequest(string Reason, string ProcessRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReassignInspectionReviewerRequest(
    Guid ReviewerUserId,
    string Reason,
    string InspectionRowVersion,
    string ProcessRowVersion);
