using System.Globalization;
using System.Text;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Rfis.Interfaces;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Rfis;
using MediatR;

namespace ConstructionInspection.Application.Rfis.Commands;

public sealed record CreateRfiCommand(Guid ConstructionObjectId, string Subject, string Question) : IRequest<RfiWorkflowResultDto>;
public sealed record SaveRfiDraftCommand(Guid Id, string Subject, string Question, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;
public sealed record SubmitRfiCommand(Guid Id, Guid ResponderUserId, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;
public sealed record ReassignRfiCommand(Guid Id, Guid ResponderUserId, string Reason, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;
public sealed record RespondRfiCommand(Guid Id, string ResponseText, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;
public sealed record RequestRfiClarificationCommand(Guid Id, string Content, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;
public sealed record CloseRfiCommand(Guid Id, string? Comment, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;
public sealed record CancelRfiCommand(Guid Id, string Reason, string RfiRowVersion, string ProcessRowVersion) : IRequest<RfiWorkflowResultDto>;

public sealed class CreateRfiCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository,
    IRfiNumberSequence numbers, IUnitOfWork unitOfWork) : IRequestHandler<CreateRfiCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(CreateRfiCommand request, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireInspector(user);
        RfiCommandChecks.EnsureUtf8(request.Question, "Question");
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var target = await repository.GetObjectForUpdateAsync(request.ConstructionObjectId, token)
                ?? throw new ResourceNotFoundException("rfi.object_not_found", "The Construction Object was not found.");
            if (!target.IsActive) throw new ConflictException("rfi.object_inactive", "The Construction Object is inactive.");
            var now = clock.UtcNow;
            var sequence = await numbers.NextAsync(token);
            var number = $"RFI-{now.Year.ToString("0000", CultureInfo.InvariantCulture)}-{sequence.ToString("D6", CultureInfo.InvariantCulture)}";
            var rfi = Rfi.Create(Guid.NewGuid(), number, target.Id, request.Subject, request.Question, actor, now);
            var process = BusinessProcess.CreateForRfi(Guid.NewGuid(), rfi.Id, actor, now);
            repository.AddRfi(rfi);
            repository.AddFoundation(process, ProcessHistory.CreateRfiInitial(Guid.NewGuid(), process.Id, actor, now));
            await unitOfWork.SaveChangesAsync(token);
            return RfiCommandChecks.Result(rfi, process);
        }, ct);
    }
}

public sealed class SaveRfiDraftCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<SaveRfiDraftCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(SaveRfiDraftCommand r, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireInspector(user);
        RfiCommandChecks.EnsureUtf8(r.Question, "Question");
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var a = await RfiCommandChecks.Owner(repository, r.Id, actor, token);
            RfiCommandChecks.Match(a, r.RfiRowVersion, r.ProcessRowVersion);
            if (a.Process.Status != ProcessStatus.Draft) throw new ConflictException("rfi.invalid_state", "Only Draft RFIs can be edited.");
            a.Rfi.SaveDraft(r.Subject, r.Question, actor, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(token);
            return RfiCommandChecks.Result(a.Rfi, a.Process);
        }, ct);
    }
}

public sealed class SubmitRfiCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository,
    IRfiReviewerService reviewers, IUnitOfWork unitOfWork) : IRequestHandler<SubmitRfiCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(SubmitRfiCommand r, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireInspector(user);
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var a = await RfiCommandChecks.Owner(repository, r.Id, actor, token);
            RfiCommandChecks.Match(a, r.RfiRowVersion, r.ProcessRowVersion);
            await RfiCommandChecks.EligibleReviewer(reviewers, r.ResponderUserId, actor, token);
            var now = clock.UtcNow;
            var from = a.Process.SubmitRfi(r.ResponderUserId, now);
            repository.AddHistory(ProcessHistory.CreateForRfi(Guid.NewGuid(), a.Process.Id, from,
                a.Process.Status, "Submit", actor, now, newAssignedToUserId: r.ResponderUserId));
            await unitOfWork.SaveChangesAsync(token);
            return RfiCommandChecks.Result(a.Rfi, a.Process);
        }, ct);
    }
}

public sealed class ReassignRfiCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository,
    IRfiReviewerService reviewers, IUnitOfWork unitOfWork) : IRequestHandler<ReassignRfiCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(ReassignRfiCommand r, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireManager(user);
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var a = await RfiCommandChecks.Any(repository, r.Id, token);
            RfiCommandChecks.Match(a, r.RfiRowVersion, r.ProcessRowVersion);
            await RfiCommandChecks.EligibleReviewer(reviewers, r.ResponderUserId, a.Rfi.CreatedByUserId, token);
            var now = clock.UtcNow;
            var change = a.Process.ReassignRfi(r.ResponderUserId, r.Reason, now);
            repository.AddHistory(ProcessHistory.CreateForRfi(Guid.NewGuid(), a.Process.Id, change.FromStatus,
                a.Process.Status, "Reassign", actor, now, r.Reason,
                previousAssignedToUserId: change.PreviousReviewerUserId, newAssignedToUserId: r.ResponderUserId));
            await unitOfWork.SaveChangesAsync(token);
            return RfiCommandChecks.Result(a.Rfi, a.Process);
        }, ct);
    }
}

public sealed class RespondRfiCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository,
    IRfiReviewerService reviewers, IUnitOfWork unitOfWork) : IRequestHandler<RespondRfiCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(RespondRfiCommand r, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireReviewer(user);
        RfiCommandChecks.EnsureUtf8(r.ResponseText, "Response");
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var a = await RfiCommandChecks.Any(repository, r.Id, token);
            if (a.Process.AssignedToUserId != actor) throw new ResourceNotFoundException("rfi.not_found", "The RFI was not found.");
            RfiCommandChecks.Match(a, r.RfiRowVersion, r.ProcessRowVersion);
            await RfiCommandChecks.EligibleReviewer(reviewers, actor, a.Rfi.CreatedByUserId, token);
            var now = clock.UtcNow;
            var entry = a.Rfi.AppendEntry(Guid.NewGuid(), RfiEntryType.Response, r.ResponseText, actor, now);
            var from = a.Process.RespondRfi(actor, now);
            repository.AddEntry(entry);
            repository.AddHistory(ProcessHistory.CreateForRfi(Guid.NewGuid(), a.Process.Id, from,
                a.Process.Status, "Respond", actor, now, rfiEntryId: entry.Id));
            await unitOfWork.SaveChangesAsync(token);
            return RfiCommandChecks.Result(a.Rfi, a.Process, entry);
        }, ct);
    }
}

public sealed class RequestRfiClarificationCommandHandler(ICurrentUser user, IClock clock,
    IRfiRepository repository, IUnitOfWork unitOfWork) : IRequestHandler<RequestRfiClarificationCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(RequestRfiClarificationCommand r, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireInspector(user);
        RfiCommandChecks.EnsureUtf8(r.Content, "Clarification");
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var a = await RfiCommandChecks.Owner(repository, r.Id, actor, token);
            RfiCommandChecks.Match(a, r.RfiRowVersion, r.ProcessRowVersion);
            var now = clock.UtcNow;
            var entry = a.Rfi.AppendEntry(Guid.NewGuid(), RfiEntryType.ClarificationRequest, r.Content, actor, now);
            var from = a.Process.RequestRfiClarification(now);
            repository.AddEntry(entry);
            repository.AddHistory(ProcessHistory.CreateForRfi(Guid.NewGuid(), a.Process.Id, from,
                a.Process.Status, "RequestClarification", actor, now, rfiEntryId: entry.Id));
            await unitOfWork.SaveChangesAsync(token);
            return RfiCommandChecks.Result(a.Rfi, a.Process, entry);
        }, ct);
    }
}

public sealed class CloseRfiCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CloseRfiCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(CloseRfiCommand r, CancellationToken ct) =>
        RfiCommandChecks.OwnerTransition(user, clock, repository, unitOfWork, r.Id, r.RfiRowVersion,
            r.ProcessRowVersion, "Close", r.Comment, (p, now) => p.CloseRfi(now), ct);
}
public sealed class CancelRfiCommandHandler(ICurrentUser user, IClock clock, IRfiRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<CancelRfiCommand, RfiWorkflowResultDto>
{
    public Task<RfiWorkflowResultDto> Handle(CancelRfiCommand r, CancellationToken ct) =>
        RfiCommandChecks.OwnerTransition(user, clock, repository, unitOfWork, r.Id, r.RfiRowVersion,
            r.ProcessRowVersion, "Cancel", r.Reason, (p, now) => p.CancelRfi(r.Reason, now), ct);
}

internal static class RfiCommandChecks
{
    public static void EnsureUtf8(string value, string field)
    {
        if (Encoding.UTF8.GetByteCount(value?.Trim() ?? string.Empty) > RfiRules.MaximumContentUtf8Bytes)
            throw new PayloadTooLargeException($"{field} exceeds {RfiRules.MaximumContentUtf8Bytes} UTF-8 bytes.", "rfi.payload_too_large");
    }
    public static async Task<RfiWriteAggregate> Any(IRfiRepository repository, Guid id, CancellationToken ct) =>
        await repository.GetForUpdateAsync(id, ct) ?? throw new ResourceNotFoundException("rfi.not_found", "The RFI was not found.");
    public static async Task<RfiWriteAggregate> Owner(IRfiRepository repository, Guid id, Guid actor, CancellationToken ct)
    {
        var a = await Any(repository, id, ct);
        if (a.Rfi.CreatedByUserId != actor) throw new ResourceNotFoundException("rfi.not_found", "The RFI was not found.");
        return a;
    }
    public static void Match(RfiWriteAggregate a, string rfiVersion, string processVersion)
    {
        RfiRowVersion.Match(a.Rfi.RowVersion, rfiVersion, nameof(rfiVersion));
        RfiRowVersion.Match(a.Process.RowVersion, processVersion, nameof(processVersion));
    }
    public static async Task EligibleReviewer(IRfiReviewerService service, Guid reviewerId, Guid creatorId, CancellationToken ct)
    {
        var reviewer = await service.GetForAssignmentAsync(reviewerId, ct);
        if (reviewer is null || !reviewer.IsActive || !reviewer.HasReviewerRole || reviewerId == creatorId)
            throw new ConflictException("rfi.reviewer_invalid", "The selected Reviewer is not eligible.");
    }
    public static RfiWorkflowResultDto Result(Rfi rfi, BusinessProcess process, RfiEntry? entry = null) =>
        new(rfi.Id, process.Status.ToRfiStatus(), process.AssignedToUserId, process.DueAt,
            process.CompletedAt, Convert.ToBase64String(rfi.RowVersion), Convert.ToBase64String(process.RowVersion),
            entry is null ? null : new(entry.Id, entry.EntryNumber, entry.EntryType, entry.Content,
                entry.CreatedByUserId, string.Empty, entry.CreatedAtUtc));
    public static Task<RfiWorkflowResultDto> OwnerTransition(ICurrentUser user, IClock clock,
        IRfiRepository repository, IUnitOfWork unitOfWork, Guid id, string rfiVersion,
        string processVersion, string action, string? note,
        Func<BusinessProcess, DateTime, ProcessStatus> transition, CancellationToken ct)
    {
        var actor = RfiAuthorization.RequireInspector(user);
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var a = await Owner(repository, id, actor, token);
            Match(a, rfiVersion, processVersion);
            var now = clock.UtcNow;
            var from = transition(a.Process, now);
            repository.AddHistory(ProcessHistory.CreateForRfi(Guid.NewGuid(), a.Process.Id, from,
                a.Process.Status, action, actor, now, note));
            await unitOfWork.SaveChangesAsync(token);
            return Result(a.Rfi, a.Process);
        }, ct);
    }
}
