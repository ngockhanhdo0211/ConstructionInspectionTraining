using ConstructionInspection.Application.Checklists.Interfaces;
using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;
using MediatR;

namespace ConstructionInspection.Application.Checklists.Commands;

public sealed record CreateChecklistItemInput(string Criterion, bool IsRequired);
public sealed record CreateChecklistCommand(Guid ConstructionObjectId, string Title, string? Description,
    IReadOnlyList<CreateChecklistItemInput> Items) : IRequest<ChecklistWorkflowResultDto>;
public sealed record StartChecklistCommand(Guid Id, string ChecklistRowVersion, string ProcessRowVersion)
    : IRequest<ChecklistWorkflowResultDto>;
public sealed record ChecklistItemInput(Guid? Id, string? RowVersion, string Criterion, bool IsRequired,
    ChecklistItemResult Result, string? Note);
public sealed record UpdateChecklistItemsCommand(Guid Id, string ChecklistRowVersion, string ProcessRowVersion,
    IReadOnlyList<ChecklistItemInput> Items) : IRequest<ChecklistItemsResultDto>;
public sealed record ReplaceChecklistInspectionsCommand(Guid Id, IReadOnlyList<Guid> InspectionIds,
    string ChecklistRowVersion, string ProcessRowVersion) : IRequest<ChecklistLinksResultDto>;
public sealed record SubmitChecklistCommand(Guid Id, Guid ReviewerUserId,
    string ChecklistRowVersion, string ProcessRowVersion) : IRequest<ChecklistWorkflowResultDto>;
public sealed record StartChecklistReviewCommand(Guid Id, string ProcessRowVersion) : IRequest<ChecklistWorkflowResultDto>;
public sealed record ApproveChecklistCommand(Guid Id, string? Comment, string ProcessRowVersion) : IRequest<ChecklistWorkflowResultDto>;
public sealed record RejectChecklistCommand(Guid Id, string Reason, string ProcessRowVersion) : IRequest<ChecklistWorkflowResultDto>;
public sealed record RequestChecklistRecheckCommand(Guid Id, string Reason, string ProcessRowVersion) : IRequest<ChecklistWorkflowResultDto>;

public sealed class CreateChecklistCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IChecklistNumberSequence numberSequence, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateChecklistCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(CreateChecklistCommand request, CancellationToken cancellationToken)
    {
        var actorId = ChecklistAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var target = await repository.GetObjectForUpdateAsync(request.ConstructionObjectId, ct)
                ?? throw new ResourceNotFoundException("checklist.object_not_found", "The Construction Object was not found.");
            if (!target.IsActive)
                throw new ConflictException("checklist.object_inactive", "An inactive Construction Object cannot receive a new Checklist.");
            var now = clock.UtcNow;
            var number = ChecklistRules.FormatNumber(now, await numberSequence.NextAsync(ct));
            var checklist = Checklist.Create(Guid.NewGuid(), number, target.Id, request.Title, request.Description,
                actorId, now, request.Items.Select(x => (x.Criterion, x.IsRequired)).ToArray());
            var process = BusinessProcess.CreateForChecklist(Guid.NewGuid(), checklist.Id, actorId, now);
            repository.AddChecklist(checklist);
            repository.AddItems(checklist.Items);
            repository.AddFoundation(process, ProcessHistory.CreateChecklistInitial(Guid.NewGuid(), process.Id, actorId, now));
            await unitOfWork.SaveChangesAsync(ct);
            return ChecklistCommandChecks.ToResult(checklist, process, null);
        }, cancellationToken);
    }
}

public sealed class StartChecklistCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<StartChecklistCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(StartChecklistCommand request, CancellationToken cancellationToken)
    {
        var actorId = ChecklistAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await ChecklistCommandChecks.RequireOwnerAsync(repository, request.Id, actorId, ct);
            ChecklistCommandChecks.Match(aggregate, request.ChecklistRowVersion, request.ProcessRowVersion);
            var now = clock.UtcNow;
            var from = aggregate.Process.Status;
            var action = from == ProcessStatus.Draft ? "Start" : "Resume";
            if (from == ProcessStatus.Draft) aggregate.Process.StartChecklist(now);
            else aggregate.Process.ResumeChecklist(now);
            aggregate.Checklist.Touch(actorId, now);
            repository.AddHistory(ProcessHistory.CreateForChecklist(Guid.NewGuid(), aggregate.Process.Id,
                from, aggregate.Process.Status, action, actorId, now));
            await unitOfWork.SaveChangesAsync(ct);
            return ChecklistCommandChecks.ToResult(aggregate.Checklist, aggregate.Process, null);
        }, cancellationToken);
    }
}

public sealed class UpdateChecklistItemsCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateChecklistItemsCommand, ChecklistItemsResultDto>
{
    public Task<ChecklistItemsResultDto> Handle(UpdateChecklistItemsCommand request, CancellationToken cancellationToken)
    {
        var actorId = ChecklistAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await ChecklistCommandChecks.RequireOwnerAsync(repository, request.Id, actorId, ct);
            ChecklistCommandChecks.Match(aggregate, request.ChecklistRowVersion, request.ProcessRowVersion);
            if (aggregate.Process.Status is not (ProcessStatus.Draft or ProcessStatus.InProgress))
                throw new ConflictException("checklist.invalid_state", "Items can only be changed in Draft or InProgress.");
            if (request.Items.Count is < 1 or > ChecklistRules.MaximumItems)
                throw new BadRequestException("checklist.item_count_invalid", "Checklist must contain between 1 and 200 items.");

            var now = clock.UtcNow;
            var existing = aggregate.Items.ToDictionary(x => x.Id);
            var seenIds = new HashSet<Guid>();
            for (var index = 0; index < request.Items.Count; index++)
            {
                var input = request.Items[index];
                if (!input.Id.HasValue) continue;
                if (!seenIds.Add(input.Id.Value)
                    || !existing.TryGetValue(input.Id.Value, out var current)
                    || string.IsNullOrWhiteSpace(input.RowVersion))
                    throw new ConflictException("checklist.item_conflict", "An existing Checklist item is missing, duplicated or stale.");
                ChecklistRowVersion.EnsureMatches(current.RowVersion, input.RowVersion, nameof(input.RowVersion));
                if (aggregate.Checklist.CurrentSubmissionId.HasValue
                    && (current.Sequence != index + 1 || current.Criterion != input.Criterion.Trim() || current.IsRequired != input.IsRequired))
                    throw new ConflictException("checklist.structure_immutable", "Checklist item structure is immutable after first submit.");
            }
            if (aggregate.Checklist.CurrentSubmissionId.HasValue
                && (request.Items.Any(x => !x.Id.HasValue) || seenIds.Count != existing.Count))
                throw new ConflictException("checklist.structure_immutable", "Checklist item structure is immutable after first submit.");

            // SQL Server unique indexes are immediate, so move existing sequences out of the
            // canonical 1..200 range before a reorder. Both saves remain in this transaction.
            if (!aggregate.Checklist.CurrentSubmissionId.HasValue && aggregate.Items.Count != 0)
            {
                var offset = ChecklistRules.MaximumItems + 1;
                foreach (var item in aggregate.Items.OrderBy(x => x.Sequence))
                    item.Author(offset++, item.Criterion, item.IsRequired, item.Result, item.Note, actorId, now);
                await unitOfWork.SaveChangesAsync(ct);
            }

            existing = aggregate.Items.ToDictionary(x => x.Id);
            var replacement = new List<ChecklistItem>(request.Items.Count);
            for (var index = 0; index < request.Items.Count; index++)
            {
                var input = request.Items[index];
                if (input.Id.HasValue)
                {
                    var item = existing[input.Id.Value];
                    existing.Remove(input.Id.Value);
                    if (aggregate.Checklist.CurrentSubmissionId.HasValue)
                        item.UpdateResult(input.Result, input.Note, actorId, now);
                    else
                        item.Author(index + 1, input.Criterion, input.IsRequired, input.Result, input.Note, actorId, now);
                    replacement.Add(item);
                }
                else
                {
                    if (aggregate.Checklist.CurrentSubmissionId.HasValue)
                        throw new ConflictException("checklist.structure_immutable", "Checklist item structure is immutable after first submit.");
                    var item = ChecklistItem.Create(Guid.NewGuid(), aggregate.Checklist.Id, index + 1,
                        input.Criterion, input.IsRequired, actorId, now);
                    if (input.Result != ChecklistItemResult.NotChecked || !string.IsNullOrWhiteSpace(input.Note))
                        item.UpdateResult(input.Result, input.Note, actorId, now);
                    replacement.Add(item);
                }
            }
            if (aggregate.Checklist.CurrentSubmissionId.HasValue && existing.Count != 0)
                throw new ConflictException("checklist.structure_immutable", "Checklist items cannot be removed after first submit.");
            repository.RemoveItems(existing.Values);
            repository.AddItems(replacement.Where(x => !aggregate.Items.Any(old => old.Id == x.Id)));
            aggregate.Checklist.ReplaceItems(replacement, actorId, now);
            await unitOfWork.SaveChangesAsync(ct);
            return new ChecklistItemsResultDto(replacement.OrderBy(x => x.Sequence).Select(ChecklistCommandChecks.ToItem).ToArray(),
                Convert.ToBase64String(aggregate.Checklist.RowVersion), Convert.ToBase64String(aggregate.Process.RowVersion));
        }, cancellationToken);
    }
}

public sealed class ReplaceChecklistInspectionsCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<ReplaceChecklistInspectionsCommand, ChecklistLinksResultDto>
{
    public Task<ChecklistLinksResultDto> Handle(ReplaceChecklistInspectionsCommand request, CancellationToken cancellationToken)
    {
        var actorId = ChecklistAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await ChecklistCommandChecks.RequireOwnerAsync(repository, request.Id, actorId, ct);
            ChecklistCommandChecks.Match(aggregate, request.ChecklistRowVersion, request.ProcessRowVersion);
            if (aggregate.Process.Status is not (ProcessStatus.Draft or ProcessStatus.InProgress))
                throw new ConflictException("checklist.invalid_state", "Inspection links can only change in Draft or InProgress.");
            if (request.InspectionIds.Count != request.InspectionIds.Distinct().Count())
                throw new ConflictException("checklist.link_duplicate", "Inspection links must be unique.");
            var target = await repository.GetObjectForUpdateAsync(aggregate.Checklist.ConstructionObjectId, ct);
            if (target is null || !target.IsActive)
                throw new ConflictException("checklist.object_inactive", "Inspection links cannot change for an inactive object.");
            var inspections = await repository.GetInspectionsAsync(request.InspectionIds, ct);
            if (inspections.Count != request.InspectionIds.Count
                || inspections.Any(x => x.ConstructionObjectId != aggregate.Checklist.ConstructionObjectId || x.CreatedByUserId != actorId))
                throw new ResourceNotFoundException("checklist.inspection_not_found", "A linked Inspection was not found in scope.");
            var now = clock.UtcNow;
            var links = request.InspectionIds.Select(x => ChecklistInspection.Create(aggregate.Checklist.Id, x, actorId, now)).ToArray();
            var changed = !aggregate.Links.Select(x => x.InspectionId).Order().SequenceEqual(request.InspectionIds.Order());
            if (changed)
            {
                repository.ReplaceLinks(aggregate.Links, links);
                aggregate.Checklist.Touch(actorId, now);
                repository.AddHistory(ProcessHistory.CreateForChecklist(Guid.NewGuid(), aggregate.Process.Id,
                    aggregate.Process.Status, aggregate.Process.Status, "UpdateInspectionLinks", actorId, now));
                await unitOfWork.SaveChangesAsync(ct);
            }
            return new ChecklistLinksResultDto(inspections.Select(x => new ChecklistInspectionDto(x.Id, string.Empty, string.Empty)).ToArray(),
                Convert.ToBase64String(aggregate.Checklist.RowVersion), Convert.ToBase64String(aggregate.Process.RowVersion));
        }, cancellationToken);
    }
}

public sealed class SubmitChecklistCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IInspectionReviewerService reviewerService, IUnitOfWork unitOfWork)
    : IRequestHandler<SubmitChecklistCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(SubmitChecklistCommand request, CancellationToken cancellationToken)
    {
        var actorId = ChecklistAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await ChecklistCommandChecks.RequireOwnerAsync(repository, request.Id, actorId, ct);
            ChecklistCommandChecks.Match(aggregate, request.ChecklistRowVersion, request.ProcessRowVersion);
            if (aggregate.Items.Count is < 1 or > ChecklistRules.MaximumItems)
                throw new BadRequestException("checklist.item_count_invalid", "Checklist must contain between 1 and 200 items.");
            foreach (var item in aggregate.Items) item.ValidateForSubmit();
            if (aggregate.Items.Any(x => x.Result == ChecklistItemResult.Fail) && aggregate.Links.Count == 0)
                throw new ConflictException("checklist.inspection_required", "A failed Checklist requires at least one linked Inspection.");
            var reviewer = await reviewerService.GetForAssignmentAsync(request.ReviewerUserId, ct);
            if (reviewer is null || !reviewer.IsActive || !reviewer.HasReviewerRole)
                throw new ConflictException("checklist.reviewer_invalid", "Reviewer must be active and have the Reviewer role.");
            if (reviewer.UserId == actorId)
                throw new ConflictException("checklist.self_review_forbidden", "The creator cannot review their own Checklist.");

            var now = clock.UtcNow;
            var submissionId = Guid.NewGuid();
            var number = aggregate.Checklist.AllocateSubmission(submissionId, reviewer.UserId, actorId, now);
            var submission = ChecklistSubmission.Create(submissionId, aggregate.Checklist.Id, number, actorId, now);
            var itemSnapshots = aggregate.Items.OrderBy(x => x.Sequence)
                .Select(x => ChecklistSubmissionItem.Capture(Guid.NewGuid(), submissionId, x)).ToArray();
            var linkSnapshots = aggregate.Links.Select(x => ChecklistSubmissionInspection.Capture(submissionId, x.InspectionId)).ToArray();
            repository.AddSubmission(submission, itemSnapshots, linkSnapshots);
            await unitOfWork.SaveChangesAsync(ct);
            var from = aggregate.Process.SubmitChecklist(reviewer.UserId, now);
            repository.AddHistory(ProcessHistory.CreateForChecklist(Guid.NewGuid(), aggregate.Process.Id,
                from, aggregate.Process.Status, "Submit", actorId, now, submissionId,
                newAssignedToUserId: aggregate.Checklist.NextSubmissionNumber == 2 ? reviewer.UserId : null));
            await unitOfWork.SaveChangesAsync(ct);
            return ChecklistCommandChecks.ToResult(aggregate.Checklist, aggregate.Process, number);
        }, cancellationToken);
    }
}

public sealed class StartChecklistReviewCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IInspectionReviewerService reviewerService, IUnitOfWork unitOfWork)
    : IRequestHandler<StartChecklistReviewCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(StartChecklistReviewCommand request, CancellationToken cancellationToken) =>
        ChecklistCommandChecks.ReviewerTransition(currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "StartReview", null,
            (process, actor, now) => process.StartChecklistReview(actor, now), cancellationToken);
}

public sealed class ApproveChecklistCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IInspectionReviewerService reviewerService, IUnitOfWork unitOfWork)
    : IRequestHandler<ApproveChecklistCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(ApproveChecklistCommand request, CancellationToken cancellationToken) =>
        ChecklistCommandChecks.ReviewerTransition(currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "Approve", request.Comment,
            (process, actor, now) => process.ApproveChecklist(actor, now), cancellationToken);
}

public sealed class RejectChecklistCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IInspectionReviewerService reviewerService, IUnitOfWork unitOfWork)
    : IRequestHandler<RejectChecklistCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(RejectChecklistCommand request, CancellationToken cancellationToken) =>
        ChecklistCommandChecks.ReviewerTransition(currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "Reject", request.Reason,
            (process, actor, now) => process.RejectChecklist(actor, request.Reason, now), cancellationToken);
}

public sealed class RequestChecklistRecheckCommandHandler(ICurrentUser currentUser, IClock clock,
    IChecklistRepository repository, IInspectionReviewerService reviewerService, IUnitOfWork unitOfWork)
    : IRequestHandler<RequestChecklistRecheckCommand, ChecklistWorkflowResultDto>
{
    public Task<ChecklistWorkflowResultDto> Handle(RequestChecklistRecheckCommand request, CancellationToken cancellationToken) =>
        ChecklistCommandChecks.ReviewerTransition(currentUser, clock, repository, reviewerService, unitOfWork,
            request.Id, request.ProcessRowVersion, "RequestRecheck", request.Reason,
            (process, actor, now) => process.RequestChecklistRecheck(actor, request.Reason, now), cancellationToken);
}

internal static class ChecklistCommandChecks
{
    public static async Task<ChecklistWriteAggregate> RequireOwnerAsync(IChecklistRepository repository,
        Guid id, Guid actorId, CancellationToken cancellationToken)
    {
        var aggregate = await repository.GetForUpdateAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
        if (aggregate.Checklist.CreatedByUserId != actorId)
            throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
        return aggregate;
    }

    public static void Match(ChecklistWriteAggregate aggregate, string checklistRowVersion, string processRowVersion)
    {
        ChecklistRowVersion.EnsureMatches(aggregate.Checklist.RowVersion, checklistRowVersion, nameof(checklistRowVersion));
        ChecklistRowVersion.EnsureMatches(aggregate.Process.RowVersion, processRowVersion, nameof(processRowVersion));
    }

    public static Task<ChecklistWorkflowResultDto> ReviewerTransition(ICurrentUser currentUser, IClock clock,
        IChecklistRepository repository, IInspectionReviewerService reviewerService, IUnitOfWork unitOfWork,
        Guid checklistId, string processRowVersion, string action, string? note,
        Func<BusinessProcess, Guid, DateTime, ProcessStatus> transition, CancellationToken cancellationToken)
    {
        var actorId = ChecklistAuthorization.RequireReviewer(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await repository.GetForUpdateAsync(checklistId, ct)
                ?? throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
            if (aggregate.Checklist.ReviewerUserId != actorId || aggregate.Process.AssignedToUserId != actorId)
                throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
            var reviewer = await reviewerService.GetForAssignmentAsync(actorId, ct);
            if (reviewer is null || !reviewer.IsActive || !reviewer.HasReviewerRole
                || aggregate.Checklist.CreatedByUserId == actorId)
                throw new ConflictException("checklist.reviewer_invalid", "The assigned Reviewer is no longer eligible.");
            ChecklistRowVersion.EnsureMatches(aggregate.Process.RowVersion, processRowVersion, nameof(processRowVersion));
            if (!aggregate.Checklist.CurrentSubmissionId.HasValue)
                throw new ConflictException("checklist.submission_missing", "The current Checklist submission is missing.");
            var now = clock.UtcNow;
            var from = transition(aggregate.Process, actorId, now);
            repository.AddHistory(ProcessHistory.CreateForChecklist(Guid.NewGuid(), aggregate.Process.Id,
                from, aggregate.Process.Status, action, actorId, now,
                aggregate.Checklist.CurrentSubmissionId, note));
            await unitOfWork.SaveChangesAsync(ct);
            return ToResult(aggregate.Checklist, aggregate.Process, null);
        }, cancellationToken);
    }

    public static ChecklistItemDto ToItem(ChecklistItem item) => new(item.Id, item.Sequence,
        item.Criterion, item.IsRequired, item.Result, item.Note, Convert.ToBase64String(item.RowVersion));

    public static ChecklistWorkflowResultDto ToResult(Checklist checklist, BusinessProcess process, int? submissionNumber) =>
        new(checklist.Id, process.Status.ToChecklistStatus(), checklist.ReviewerUserId,
            checklist.CurrentSubmissionId, submissionNumber, process.CompletedAt,
            Convert.ToBase64String(checklist.RowVersion), Convert.ToBase64String(process.RowVersion));
}
