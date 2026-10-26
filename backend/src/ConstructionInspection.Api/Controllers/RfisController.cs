using System.Text.Json.Serialization;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Rfis.Commands;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Application.Rfis.Queries;
using ConstructionInspection.Domain.Rfis;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/rfis")]
public sealed class RfisController(ISender sender) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<RfiListItemDto>> Get([FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] RfiStatus? status = null, [FromQuery] Guid? constructionObjectId = null,
        [FromQuery] bool assignedToMe = false, [FromQuery] bool? overdue = null,
        [FromQuery] string sortBy = "createdAt", [FromQuery] string sortDirection = "desc",
        CancellationToken cancellationToken = default) => sender.Send(new GetRfisQuery(pageNumber,
            pageSize, search, status, constructionObjectId, assignedToMe, overdue, sortBy, sortDirection), cancellationToken);

    [HttpGet("responder-options")]
    [Authorize(Roles = ApplicationRoles.Inspector + "," + ApplicationRoles.Manager)]
    public Task<PagedResult<RfiResponderOptionDto>> GetResponderOptions([FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetRfiResponderOptionsQuery(pageNumber, pageSize, search), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<RfiDetailDto> GetDetail(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetRfiDetailQuery(id), cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [RequestSizeLimit(131_072)]
    public async Task<ActionResult<RfiWorkflowResultDto>> Create(CreateRfiRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateRfiCommand(request.ConstructionObjectId, request.Subject, request.Question), cancellationToken);
        return CreatedAtAction(nameof(GetDetail), new { id = result.RfiId }, result);
    }

    [HttpPut("{id:guid}/draft")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [RequestSizeLimit(131_072)]
    public Task<RfiWorkflowResultDto> SaveDraft(Guid id, SaveRfiDraftRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SaveRfiDraftCommand(id, request.Subject, request.Question, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    public Task<RfiWorkflowResultDto> Submit(Guid id, SubmitRfiRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SubmitRfiCommand(id, request.ResponderUserId, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = ApplicationRoles.Manager)]
    public Task<RfiWorkflowResultDto> Reassign(Guid id, ReassignRfiRequest request, CancellationToken cancellationToken) =>
        sender.Send(new ReassignRfiCommand(id, request.ResponderUserId, request.Reason, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/responses")]
    [Authorize(Roles = ApplicationRoles.Reviewer)]
    [RequestSizeLimit(131_072)]
    public async Task<ActionResult<RfiWorkflowResultDto>> Respond(
        Guid id, RespondRfiRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RespondRfiCommand(
            id, request.ResponseText, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);
        return CreatedAtAction(nameof(GetDetail), new { id }, result);
    }

    [HttpPost("{id:guid}/request-clarification")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    [RequestSizeLimit(131_072)]
    public async Task<ActionResult<RfiWorkflowResultDto>> RequestClarification(
        Guid id, ClarifyRfiRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RequestRfiClarificationCommand(
            id, request.Content, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);
        return CreatedAtAction(nameof(GetDetail), new { id }, result);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    public Task<RfiWorkflowResultDto> Close(Guid id, CloseRfiRequest request, CancellationToken cancellationToken) =>
        sender.Send(new CloseRfiCommand(id, request.Comment, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = ApplicationRoles.Inspector)]
    public Task<RfiWorkflowResultDto> Cancel(Guid id, CancelRfiRequest request, CancellationToken cancellationToken) =>
        sender.Send(new CancelRfiCommand(id, request.Reason, request.RfiRowVersion, request.ProcessRowVersion), cancellationToken);

    [HttpGet("{id:guid}/history")]
    public Task<PagedResult<RfiHistoryDto>> GetHistory(Guid id, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        sender.Send(new GetRfiHistoryQuery(id, pageNumber, pageSize), cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateRfiRequest(Guid ConstructionObjectId, string Subject, string Question);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveRfiDraftRequest(string Subject, string Question, string RfiRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitRfiRequest(Guid ResponderUserId, string RfiRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReassignRfiRequest(Guid ResponderUserId, string Reason, string RfiRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RespondRfiRequest(string ResponseText, string RfiRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClarifyRfiRequest(string Content, string RfiRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CloseRfiRequest(string? Comment, string RfiRowVersion, string ProcessRowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelRfiRequest(string Reason, string RfiRowVersion, string ProcessRowVersion);
