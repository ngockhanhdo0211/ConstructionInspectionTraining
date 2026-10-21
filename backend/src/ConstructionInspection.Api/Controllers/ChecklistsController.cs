using System.Text.Json.Serialization;
using ConstructionInspection.Application.Checklists.Commands;
using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Checklists.Queries;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Domain.Checklists;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/checklists")]
public sealed class ChecklistsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ChecklistListItemDto>> Get([FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] ChecklistStatus? status = null, [FromQuery] Guid? constructionObjectId = null,
        [FromQuery] Guid? reviewerUserId = null, [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortDirection = "desc", CancellationToken cancellationToken = default) =>
        sender.Send(new GetChecklistsQuery(pageNumber, pageSize, search, status, constructionObjectId,
            reviewerUserId, sortBy, sortDirection), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ChecklistDetailDto> GetDetail(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetChecklistDetailQuery(id), cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [RequestSizeLimit(1_048_576)]
    public async Task<ActionResult<ChecklistWorkflowResultDto>> Create(CreateChecklistRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateChecklistCommand(request.ConstructionObjectId,
            request.Title, request.Description,
            request.Items.Select(x => new CreateChecklistItemInput(x.Criterion, x.IsRequired)).ToArray()), cancellationToken);
        return CreatedAtAction(nameof(GetDetail), new { id = result.ChecklistId }, result);
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    public Task<ChecklistWorkflowResultDto> Start(Guid id, StartChecklistRequest request, CancellationToken cancellationToken) =>
        sender.Send(new StartChecklistCommand(id, request.ChecklistRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPut("{id:guid}/items")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [RequestSizeLimit(1_048_576)]
    public Task<ChecklistItemsResultDto> UpdateItems(Guid id, UpdateChecklistItemsRequest request,
        CancellationToken cancellationToken) => sender.Send(new UpdateChecklistItemsCommand(id,
            request.ChecklistRowVersion, request.ProcessRowVersion,
            request.Items.Select(x => new ChecklistItemInput(x.Id, x.RowVersion, x.Criterion,
                x.IsRequired, x.Result, x.Note)).ToArray()), cancellationToken);

    [HttpPut("{id:guid}/inspections")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    public Task<ChecklistLinksResultDto> ReplaceInspections(Guid id, ReplaceChecklistInspectionsRequest request,
        CancellationToken cancellationToken) => sender.Send(new ReplaceChecklistInspectionsCommand(id,
            request.InspectionIds, request.ChecklistRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    public Task<ChecklistWorkflowResultDto> Submit(Guid id, SubmitChecklistRequest request,
        CancellationToken cancellationToken) => sender.Send(new SubmitChecklistCommand(id,
            request.ReviewerUserId, request.ChecklistRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/start-review")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    public Task<ChecklistWorkflowResultDto> StartReview(Guid id, ChecklistProcessRequest request,
        CancellationToken cancellationToken) => sender.Send(new StartChecklistReviewCommand(id, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    public Task<ChecklistWorkflowResultDto> Approve(Guid id, ApproveChecklistRequest request,
        CancellationToken cancellationToken) => sender.Send(new ApproveChecklistCommand(id, request.Comment, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    public Task<ChecklistWorkflowResultDto> Reject(Guid id, RejectChecklistRequest request,
        CancellationToken cancellationToken) => sender.Send(new RejectChecklistCommand(id, request.Reason, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/recheck")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    public Task<ChecklistWorkflowResultDto> Recheck(Guid id, RejectChecklistRequest request,
        CancellationToken cancellationToken) => sender.Send(new RequestChecklistRecheckCommand(id, request.Reason, request.ProcessRowVersion), cancellationToken);

    [HttpGet("{id:guid}/history")]
    public Task<PagedResult<ChecklistHistoryDto>> GetHistory(Guid id, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        sender.Send(new GetChecklistHistoryQuery(id, pageNumber, pageSize), cancellationToken);

    [HttpGet("{id:guid}/submissions")]
    public Task<PagedResult<ChecklistSubmissionListItemDto>> GetSubmissions(Guid id,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetChecklistSubmissionsQuery(id, pageNumber, pageSize), cancellationToken);

    [HttpGet("{id:guid}/submissions/{submissionNumber:int}")]
    public Task<ChecklistSubmissionDetailDto> GetSubmission(Guid id, int submissionNumber,
        CancellationToken cancellationToken) => sender.Send(new GetChecklistSubmissionQuery(id, submissionNumber), cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateChecklistRequest(Guid ConstructionObjectId, string Title, string? Description,
    IReadOnlyList<CreateChecklistItemRequest> Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateChecklistItemRequest(string Criterion, bool IsRequired);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartChecklistRequest(string ChecklistRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateChecklistItemsRequest(string ChecklistRowVersion, string ProcessRowVersion,
    IReadOnlyList<ChecklistItemRequest> Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChecklistItemRequest(Guid? Id, string? RowVersion, string Criterion, bool IsRequired,
    ChecklistItemResult Result, string? Note);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceChecklistInspectionsRequest(IReadOnlyList<Guid> InspectionIds,
    string ChecklistRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitChecklistRequest(Guid ReviewerUserId, string ChecklistRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChecklistProcessRequest(string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveChecklistRequest(string? Comment, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectChecklistRequest(string Reason, string ProcessRowVersion);
