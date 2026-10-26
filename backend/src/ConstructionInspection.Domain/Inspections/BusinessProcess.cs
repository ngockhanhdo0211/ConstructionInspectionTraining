namespace ConstructionInspection.Domain.Inspections;

using ConstructionInspection.Domain.Rfis;

public sealed class BusinessProcess
{
    private BusinessProcess() { }

    public Guid Id { get; private set; }
    public Guid? InspectionId { get; private set; }
    public Guid? ChecklistId { get; private set; }
    public Guid? RfiId { get; private set; }
    public ProcessEntityType EntityType { get; private set; }
    public ProcessStatus Status { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static BusinessProcess CreateForInspection(
        Guid id, Guid inspectionId, Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || inspectionId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "Process, Inspection and actor identifiers are required.");
        return new BusinessProcess
        {
            Id = id,
            InspectionId = inspectionId,
            EntityType = ProcessEntityType.Inspection,
            Status = ProcessStatus.Draft,
            CreatedByUserId = actorId,
            CreatedAt = InspectionRules.EnsureUtc(createdAtUtc)
        };
    }

    public static BusinessProcess CreateDraft(
        Guid id, Guid inspectionId, Guid actorId, DateTime createdAtUtc) =>
        CreateForInspection(id, inspectionId, actorId, createdAtUtc);

    public static BusinessProcess CreateForChecklist(
        Guid id, Guid checklistId, Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || checklistId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("process.invalid_identity", "Process, Checklist and actor identifiers are required.");
        return new BusinessProcess
        {
            Id = id,
            ChecklistId = checklistId,
            EntityType = ProcessEntityType.Checklist,
            Status = ProcessStatus.Draft,
            CreatedByUserId = actorId,
            CreatedAt = InspectionRules.EnsureUtc(createdAtUtc)
        };
    }

    public static BusinessProcess CreateForRfi(Guid id, Guid rfiId, Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || rfiId == Guid.Empty || actorId == Guid.Empty)
            throw RfiRules.Error("rfi.process_identity_invalid", "Process, RFI and actor identifiers are required.");
        return new BusinessProcess
        {
            Id = id,
            RfiId = rfiId,
            EntityType = ProcessEntityType.Rfi,
            Status = ProcessStatus.Draft,
            CreatedByUserId = actorId,
            CreatedAt = RfiRules.EnsureUtc(createdAtUtc)
        };
    }

    public void EnsureOwnerInvariant()
    {
        var hasInspection = InspectionId.HasValue;
        var hasChecklist = ChecklistId.HasValue;
        var hasRfi = RfiId.HasValue;
        if ((hasInspection ? 1 : 0) + (hasChecklist ? 1 : 0) + (hasRfi ? 1 : 0) != 1
            || EntityType == ProcessEntityType.Inspection && !hasInspection
            || EntityType == ProcessEntityType.Checklist && !hasChecklist
            || EntityType == ProcessEntityType.Rfi && !hasRfi)
            throw InspectionRules.Error("process.owner_invalid", "A process must have exactly one owner matching its entity type.");
    }

    public void EnsureDraft()
    {
        EnsureInspectionProcess();
        if (Status != ProcessStatus.Draft)
            throw InspectionRules.Error("inspection.invalid_state", "The Inspection must be Draft.");
    }

    public void EnsureEditable()
    {
        EnsureInspectionProcess();
        if (Status is not (ProcessStatus.Draft or ProcessStatus.Recheck))
            throw InspectionRules.Error("inspection.invalid_state", "Only Draft or Recheck Inspection data can be saved.");
    }

    public ProcessStatus Submit(Guid reviewerUserId, DateTime occurredAtUtc)
    {
        EnsureInspectionProcess();
        if (Status is not (ProcessStatus.Draft or ProcessStatus.Recheck))
            throw InspectionRules.Error("inspection.invalid_state", "Only Draft or Recheck Inspections can be submitted.");
        if (reviewerUserId == Guid.Empty)
            throw InspectionRules.Error("inspection.reviewer_invalid", "Reviewer is required.");
        if (Status == ProcessStatus.Recheck && AssignedToUserId != reviewerUserId)
            throw InspectionRules.Error("inspection.reviewer_change_forbidden", "Re-submit must keep the assigned Reviewer.");

        var from = Status;
        AssignedToUserId = reviewerUserId;
        Status = ProcessStatus.Submitted;
        UpdatedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        DueAt = null;
        CompletedAt = null;
        return from;
    }

    public ProcessStatus StartReview(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureInspectionProcess();
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.Submitted);
        return TransitionTo(ProcessStatus.UnderReview, occurredAtUtc);
    }

    public ProcessStatus Approve(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureInspectionProcess();
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.UnderReview);
        var from = TransitionTo(ProcessStatus.Approved, occurredAtUtc);
        CompletedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public ProcessStatus Reject(Guid actorId, string reason, DateTime occurredAtUtc)
    {
        EnsureInspectionProcess();
        EnsureRequiredReason(reason);
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.UnderReview);
        var from = TransitionTo(ProcessStatus.Rejected, occurredAtUtc);
        CompletedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public ProcessStatus RequestRecheck(Guid actorId, string reason, DateTime occurredAtUtc)
    {
        EnsureInspectionProcess();
        EnsureRequiredReason(reason);
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.UnderReview);
        var from = TransitionTo(ProcessStatus.Recheck, occurredAtUtc);
        CompletedAt = null;
        return from;
    }

    public (ProcessStatus FromStatus, Guid PreviousReviewerUserId) ReassignReviewer(
        Guid reviewerUserId, string reason, DateTime occurredAtUtc)
    {
        EnsureInspectionProcess();
        EnsureRequiredReason(reason);
        if (Status is not (ProcessStatus.Submitted or ProcessStatus.UnderReview))
            throw InspectionRules.Error("inspection.invalid_state", "Reviewer can only be reassigned while Submitted or UnderReview.");
        if (!AssignedToUserId.HasValue)
            throw InspectionRules.Error("inspection.reviewer_missing", "The Inspection has no assigned Reviewer.");
        if (reviewerUserId == Guid.Empty || reviewerUserId == AssignedToUserId)
            throw InspectionRules.Error("inspection.reviewer_unchanged", "The new Reviewer must differ from the current Reviewer.");

        var result = (Status, AssignedToUserId.Value);
        AssignedToUserId = reviewerUserId;
        Status = ProcessStatus.Submitted;
        UpdatedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        CompletedAt = null;
        return result;
    }

    public ProcessStatus StartChecklist(DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureStatus(ProcessStatus.Draft);
        return TransitionTo(ProcessStatus.InProgress, occurredAtUtc);
    }

    public ProcessStatus ResumeChecklist(DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureStatus(ProcessStatus.Recheck);
        return TransitionTo(ProcessStatus.InProgress, occurredAtUtc);
    }

    public ProcessStatus SubmitChecklist(Guid reviewerUserId, DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureStatus(ProcessStatus.InProgress);
        if (reviewerUserId == Guid.Empty)
            throw InspectionRules.Error("checklist.reviewer_invalid", "Reviewer is required.");
        if (AssignedToUserId.HasValue && AssignedToUserId != reviewerUserId)
            throw InspectionRules.Error("checklist.reviewer_change_forbidden", "Re-submit must keep the assigned Reviewer.");
        AssignedToUserId = reviewerUserId;
        return TransitionTo(ProcessStatus.Submitted, occurredAtUtc);
    }

    public ProcessStatus StartChecklistReview(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.Submitted);
        return TransitionTo(ProcessStatus.UnderReview, occurredAtUtc);
    }

    public ProcessStatus ApproveChecklist(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.UnderReview);
        var from = TransitionTo(ProcessStatus.Approved, occurredAtUtc);
        CompletedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public ProcessStatus RejectChecklist(Guid actorId, string reason, DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureRequiredReason(reason);
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.UnderReview);
        var from = TransitionTo(ProcessStatus.Rejected, occurredAtUtc);
        CompletedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public ProcessStatus RequestChecklistRecheck(Guid actorId, string reason, DateTime occurredAtUtc)
    {
        EnsureChecklistProcess();
        EnsureRequiredReason(reason);
        EnsureAssignedReviewer(actorId);
        EnsureStatus(ProcessStatus.UnderReview);
        var from = TransitionTo(ProcessStatus.Recheck, occurredAtUtc);
        CompletedAt = null;
        return from;
    }

    public ProcessStatus SubmitRfi(Guid reviewerUserId, DateTime occurredAtUtc)
    {
        EnsureRfiProcess();
        EnsureRfiStatus(ProcessStatus.Draft);
        if (reviewerUserId == Guid.Empty)
            throw RfiRules.Error("rfi.reviewer_invalid", "Reviewer is required.");
        var now = RfiRules.EnsureUtc(occurredAtUtc);
        AssignedToUserId = reviewerUserId;
        DueAt = now.Add(RfiRules.ResponseSla);
        CompletedAt = null;
        return TransitionRfi(ProcessStatus.Open, now);
    }

    public ProcessStatus RespondRfi(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureRfiProcess();
        EnsureRfiAssignedReviewer(actorId);
        EnsureRfiStatus(ProcessStatus.Open);
        DueAt = null;
        return TransitionRfi(ProcessStatus.Responded, occurredAtUtc);
    }

    public ProcessStatus RequestRfiClarification(DateTime occurredAtUtc)
    {
        EnsureRfiProcess();
        EnsureRfiStatus(ProcessStatus.Responded);
        var now = RfiRules.EnsureUtc(occurredAtUtc);
        if (!AssignedToUserId.HasValue)
            throw RfiRules.Error("rfi.reviewer_missing", "The RFI has no assigned Reviewer.");
        DueAt = now.Add(RfiRules.ResponseSla);
        CompletedAt = null;
        return TransitionRfi(ProcessStatus.Open, now);
    }

    public (ProcessStatus FromStatus, Guid PreviousReviewerUserId) ReassignRfi(
        Guid reviewerUserId, string reason, DateTime occurredAtUtc)
    {
        EnsureRfiProcess();
        EnsureRfiReason(reason);
        EnsureRfiStatus(ProcessStatus.Open);
        if (!AssignedToUserId.HasValue)
            throw RfiRules.Error("rfi.reviewer_missing", "The RFI has no assigned Reviewer.");
        if (reviewerUserId == Guid.Empty || reviewerUserId == AssignedToUserId)
            throw RfiRules.Error("rfi.reviewer_unchanged", "The new Reviewer must differ from the current Reviewer.");
        var now = RfiRules.EnsureUtc(occurredAtUtc);
        var result = (Status, AssignedToUserId.Value);
        AssignedToUserId = reviewerUserId;
        DueAt = now.Add(RfiRules.ResponseSla);
        UpdatedAt = now;
        return result;
    }

    public ProcessStatus CloseRfi(DateTime occurredAtUtc)
    {
        EnsureRfiProcess();
        EnsureRfiStatus(ProcessStatus.Responded);
        var from = TransitionRfi(ProcessStatus.Closed, occurredAtUtc);
        DueAt = null;
        CompletedAt = RfiRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public ProcessStatus CancelRfi(string reason, DateTime occurredAtUtc)
    {
        EnsureRfiProcess();
        EnsureRfiReason(reason);
        if (Status is not (ProcessStatus.Draft or ProcessStatus.Open))
            throw RfiRules.Error("rfi.invalid_state", "Only Draft or Open RFIs can be cancelled.");
        var from = TransitionRfi(ProcessStatus.Cancelled, occurredAtUtc);
        DueAt = null;
        CompletedAt = RfiRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    private void EnsureInspectionProcess()
    {
        EnsureOwnerInvariant();
        if (EntityType != ProcessEntityType.Inspection)
            throw InspectionRules.Error("process.entity_type_mismatch", "Checklist processes cannot use Inspection transitions.");
    }

    private void EnsureChecklistProcess()
    {
        EnsureOwnerInvariant();
        if (EntityType != ProcessEntityType.Checklist)
            throw InspectionRules.Error("process.entity_type_mismatch", "Inspection processes cannot use Checklist transitions.");
    }

    private void EnsureRfiProcess()
    {
        EnsureOwnerInvariant();
        if (EntityType != ProcessEntityType.Rfi)
            throw RfiRules.Error("process.entity_type_mismatch", "Only RFI processes can use RFI transitions.");
    }

    private void EnsureRfiAssignedReviewer(Guid actorId)
    {
        if (!AssignedToUserId.HasValue || AssignedToUserId != actorId)
            throw RfiRules.Error("rfi.not_assigned_reviewer", "Only the assigned Reviewer can respond.");
    }

    private void EnsureRfiStatus(ProcessStatus expected)
    {
        if (Status != expected)
            throw RfiRules.Error("rfi.invalid_state", $"The RFI must be {expected}.");
    }

    private ProcessStatus TransitionRfi(ProcessStatus target, DateTime occurredAtUtc)
    {
        var from = Status;
        Status = target;
        UpdatedAt = RfiRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    private static void EnsureRfiReason(string reason) =>
        RfiRules.RequiredText(reason, RfiRules.MaximumReasonLength, "rfi.reason_invalid", "Reason");

    private void EnsureAssignedReviewer(Guid actorId)
    {
        if (!AssignedToUserId.HasValue || AssignedToUserId != actorId)
            throw InspectionRules.Error("inspection.not_assigned_reviewer", "Only the assigned Reviewer can perform this action.");
    }

    private void EnsureStatus(ProcessStatus expected)
    {
        if (Status != expected)
            throw InspectionRules.Error("inspection.invalid_state", $"The Inspection must be {expected}.");
    }

    private ProcessStatus TransitionTo(ProcessStatus target, DateTime occurredAtUtc)
    {
        var from = Status;
        Status = target;
        UpdatedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    private static void EnsureRequiredReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)
            || reason.Trim().Length > InspectionRules.MaximumWorkflowNoteLength)
            throw InspectionRules.Error("inspection.reason_required", "A valid workflow reason is required.");
    }
}
