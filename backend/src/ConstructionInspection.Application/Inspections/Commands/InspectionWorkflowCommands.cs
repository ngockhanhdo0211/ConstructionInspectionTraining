using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Domain.Inspections;
using MediatR;

namespace ConstructionInspection.Application.Inspections.Commands;

public sealed record SubmitInspectionCommand(
    Guid Id, Guid ReviewerUserId, string InspectionRowVersion,
    string SubmissionRowVersion, string ProcessRowVersion)
    : IRequest<InspectionWorkflowResultDto>;

public sealed record StartInspectionReviewCommand(Guid Id, string ProcessRowVersion)
    : IRequest<InspectionWorkflowResultDto>;

public sealed record ApproveInspectionCommand(Guid Id, string? Comment, string ProcessRowVersion)
    : IRequest<InspectionWorkflowResultDto>;

public sealed record RejectInspectionCommand(Guid Id, string Reason, string ProcessRowVersion)
    : IRequest<InspectionWorkflowResultDto>;

public sealed record RequestInspectionRecheckCommand(Guid Id, string Reason, string ProcessRowVersion)
    : IRequest<InspectionWorkflowResultDto>;

public sealed record ReassignInspectionReviewerCommand(
    Guid Id, Guid ReviewerUserId, string Reason,
    string InspectionRowVersion, string ProcessRowVersion)
    : IRequest<InspectionWorkflowResultDto>;

public sealed class SubmitInspectionCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReviewerService reviewerService,
    IFormIoDataValidator dataValidator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SubmitInspectionCommand, InspectionWorkflowResultDto>
{
    public Task<InspectionWorkflowResultDto> Handle(
        SubmitInspectionCommand request, CancellationToken cancellationToken)
    {
        var actorId = InspectionAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await WorkflowChecks.GetAggregateAsync(repository, request.Id, ct);
            if (aggregate.Inspection.CreatedByUserId != actorId)
                throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");

            InspectionRowVersion.EnsureMatches(aggregate.Inspection.RowVersion, request.InspectionRowVersion, nameof(request.InspectionRowVersion));
            InspectionRowVersion.EnsureMatches(aggregate.Submission.RowVersion, request.SubmissionRowVersion, nameof(request.SubmissionRowVersion));
            InspectionRowVersion.EnsureMatches(aggregate.Process.RowVersion, request.ProcessRowVersion, nameof(request.ProcessRowVersion));
            if (aggregate.Process.Status == InspectionStatus.Recheck && aggregate.Submission.SubmittedAt.HasValue)
                throw new ConflictException("inspection.revision_required", "A new editable revision must be saved before re-submit.");

            var reviewer = await WorkflowChecks.RequireEligibleReviewerAsync(
                reviewerService, request.ReviewerUserId, aggregate.Inspection.CreatedByUserId, ct);
            var pinned = await repository.GetPinnedVersionAsync(aggregate.Inspection.FormTemplateVersionId, ct)
                ?? throw new ConflictException("inspection.form_template_unavailable", "The pinned Form Template version is unavailable.");
            dataValidator.ValidateSubmit(pinned.SchemaJson, aggregate.Submission.DataJson);

            var now = clock.UtcNow;
            var firstSubmit = aggregate.Process.Status == InspectionStatus.Draft;
            aggregate.Inspection.AssignReviewer(reviewer.UserId, now);
            var from = aggregate.Process.Submit(reviewer.UserId, now);
            aggregate.Submission.Submit(now);
            repository.AddHistory(ProcessHistory.Create(
                Guid.NewGuid(), aggregate.Process.Id, from, InspectionStatus.Submitted,
                "Submit", actorId, now, aggregate.Submission.Id,
                newAssignedToUserId: firstSubmit ? reviewer.UserId : null));
            await unitOfWork.SaveChangesAsync(ct);
            return WorkflowChecks.ToResult(aggregate);
        }, cancellationToken);
    }
}

public sealed class StartInspectionReviewCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReviewerService reviewerService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<StartInspectionReviewCommand, InspectionWorkflowResultDto>
{
    public Task<InspectionWorkflowResultDto> Handle(
        StartInspectionReviewCommand request, CancellationToken cancellationToken) =>
        WorkflowChecks.ExecuteReviewerTransitionAsync(
            currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "StartReview", null,
            (process, actorId, now) => process.StartReview(actorId, now),
            cancellationToken);
}

public sealed class ApproveInspectionCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReviewerService reviewerService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ApproveInspectionCommand, InspectionWorkflowResultDto>
{
    public Task<InspectionWorkflowResultDto> Handle(
        ApproveInspectionCommand request, CancellationToken cancellationToken) =>
        WorkflowChecks.ExecuteReviewerTransitionAsync(
            currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "Approve", request.Comment,
            (process, actorId, now) => process.Approve(actorId, now),
            cancellationToken);
}

public sealed class RejectInspectionCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReviewerService reviewerService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RejectInspectionCommand, InspectionWorkflowResultDto>
{
    public Task<InspectionWorkflowResultDto> Handle(
        RejectInspectionCommand request, CancellationToken cancellationToken) =>
        WorkflowChecks.ExecuteReviewerTransitionAsync(
            currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "Reject", request.Reason,
            (process, actorId, now) => process.Reject(actorId, request.Reason, now),
            cancellationToken);
}

public sealed class RequestInspectionRecheckCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReviewerService reviewerService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RequestInspectionRecheckCommand, InspectionWorkflowResultDto>
{
    public Task<InspectionWorkflowResultDto> Handle(
        RequestInspectionRecheckCommand request, CancellationToken cancellationToken) =>
        WorkflowChecks.ExecuteReviewerTransitionAsync(
            currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "RequestRecheck", request.Reason,
            (process, actorId, now) => process.RequestRecheck(actorId, request.Reason, now),
            cancellationToken);
}

public sealed class ReassignInspectionReviewerCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReviewerService reviewerService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ReassignInspectionReviewerCommand, InspectionWorkflowResultDto>
{
    public Task<InspectionWorkflowResultDto> Handle(
        ReassignInspectionReviewerCommand request, CancellationToken cancellationToken)
    {
        var actorId = InspectionAuthorization.RequireManagerOrAdministrator(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await WorkflowChecks.GetAggregateAsync(repository, request.Id, ct);
            InspectionRowVersion.EnsureMatches(aggregate.Inspection.RowVersion, request.InspectionRowVersion, nameof(request.InspectionRowVersion));
            InspectionRowVersion.EnsureMatches(aggregate.Process.RowVersion, request.ProcessRowVersion, nameof(request.ProcessRowVersion));
            var reviewer = await WorkflowChecks.RequireEligibleReviewerAsync(
                reviewerService, request.ReviewerUserId, aggregate.Inspection.CreatedByUserId, ct);

            var now = clock.UtcNow;
            var transition = aggregate.Process.ReassignReviewer(reviewer.UserId, request.Reason, now);
            aggregate.Inspection.ReassignReviewer(reviewer.UserId, now);
            repository.AddHistory(ProcessHistory.Create(
                Guid.NewGuid(), aggregate.Process.Id,
                transition.FromStatus, InspectionStatus.Submitted,
                "ReassignReviewer", actorId, now, aggregate.Submission.Id,
                request.Reason, transition.PreviousReviewerUserId, reviewer.UserId));
            await unitOfWork.SaveChangesAsync(ct);
            return WorkflowChecks.ToResult(aggregate);
        }, cancellationToken);
    }
}

internal static class WorkflowChecks
{
    public static async Task<InspectionWriteAggregate> GetAggregateAsync(
        IInspectionRepository repository, Guid id, CancellationToken cancellationToken) =>
        await repository.GetForUpdateAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");

    public static async Task<ReviewerEligibility> RequireEligibleReviewerAsync(
        IInspectionReviewerService reviewerService,
        Guid reviewerUserId,
        Guid creatorUserId,
        CancellationToken cancellationToken)
    {
        var reviewer = await reviewerService.GetForAssignmentAsync(reviewerUserId, cancellationToken);
        if (reviewer is null || !reviewer.IsActive || !reviewer.HasReviewerRole)
            throw new ConflictException("inspection.reviewer_invalid", "Reviewer must be an active user with the Reviewer role.");
        if (reviewer.UserId == creatorUserId)
            throw new ConflictException("inspection.self_review_forbidden", "The creator cannot review their own Inspection.");
        return reviewer;
    }

    public static Task<InspectionWorkflowResultDto> ExecuteReviewerTransitionAsync(
        ICurrentUser currentUser,
        IClock clock,
        IInspectionRepository repository,
        IInspectionReviewerService reviewerService,
        IUnitOfWork unitOfWork,
        Guid inspectionId,
        string processRowVersion,
        string action,
        string? note,
        Func<BusinessProcess, Guid, DateTime, InspectionStatus> transition,
        CancellationToken cancellationToken)
    {
        var actorId = InspectionAuthorization.RequireReviewer(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await GetAggregateAsync(repository, inspectionId, ct);
            if (aggregate.Inspection.ReviewerUserId != actorId
                || aggregate.Process.AssignedToUserId != actorId)
                throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");
            await RequireEligibleReviewerAsync(reviewerService, actorId, aggregate.Inspection.CreatedByUserId, ct);
            InspectionRowVersion.EnsureMatches(aggregate.Process.RowVersion, processRowVersion, nameof(processRowVersion));

            var now = clock.UtcNow;
            var from = transition(aggregate.Process, actorId, now);
            repository.AddHistory(ProcessHistory.Create(
                Guid.NewGuid(), aggregate.Process.Id, from, aggregate.Process.Status,
                action, actorId, now, aggregate.Submission.Id, note));
            await unitOfWork.SaveChangesAsync(ct);
            return ToResult(aggregate);
        }, cancellationToken);
    }

    public static InspectionWorkflowResultDto ToResult(InspectionWriteAggregate aggregate) => new(
        aggregate.Inspection.Id,
        aggregate.Process.Status,
        aggregate.Inspection.ReviewerUserId,
        aggregate.Submission.RevisionNumber,
        aggregate.Process.CompletedAt,
        Convert.ToBase64String(aggregate.Inspection.RowVersion),
        Convert.ToBase64String(aggregate.Submission.RowVersion),
        Convert.ToBase64String(aggregate.Process.RowVersion));
}
