namespace ConstructionInspection.Domain.Inspections;

public sealed class BusinessProcess
{
    private BusinessProcess() { }

    public Guid Id { get; private set; }
    public Guid InspectionId { get; private set; }
    public ProcessEntityType EntityType { get; private set; }
    public InspectionStatus Status { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static BusinessProcess CreateDraft(
        Guid id, Guid inspectionId, Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || inspectionId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "Process, Inspection and actor identifiers are required.");
        return new BusinessProcess
        {
            Id = id,
            InspectionId = inspectionId,
            EntityType = ProcessEntityType.Inspection,
            Status = InspectionStatus.Draft,
            CreatedByUserId = actorId,
            CreatedAt = InspectionRules.EnsureUtc(createdAtUtc)
        };
    }

    public void EnsureDraft()
    {
        if (Status != InspectionStatus.Draft)
            throw InspectionRules.Error("inspection.invalid_state", "The Inspection must be Draft.");
    }

    public void EnsureEditable()
    {
        if (Status is not (InspectionStatus.Draft or InspectionStatus.Recheck))
            throw InspectionRules.Error("inspection.invalid_state", "Only Draft or Recheck Inspection data can be saved.");
    }

    public InspectionStatus Submit(Guid reviewerUserId, DateTime occurredAtUtc)
    {
        if (Status is not (InspectionStatus.Draft or InspectionStatus.Recheck))
            throw InspectionRules.Error("inspection.invalid_state", "Only Draft or Recheck Inspections can be submitted.");
        if (reviewerUserId == Guid.Empty)
            throw InspectionRules.Error("inspection.reviewer_invalid", "Reviewer is required.");
        if (Status == InspectionStatus.Recheck && AssignedToUserId != reviewerUserId)
            throw InspectionRules.Error("inspection.reviewer_change_forbidden", "Re-submit must keep the assigned Reviewer.");

        var from = Status;
        AssignedToUserId = reviewerUserId;
        Status = InspectionStatus.Submitted;
        UpdatedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        DueAt = null;
        CompletedAt = null;
        return from;
    }

    public InspectionStatus StartReview(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureAssignedReviewer(actorId);
        EnsureStatus(InspectionStatus.Submitted);
        return TransitionTo(InspectionStatus.UnderReview, occurredAtUtc);
    }

    public InspectionStatus Approve(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureAssignedReviewer(actorId);
        EnsureStatus(InspectionStatus.UnderReview);
        var from = TransitionTo(InspectionStatus.Approved, occurredAtUtc);
        CompletedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public InspectionStatus Reject(Guid actorId, string reason, DateTime occurredAtUtc)
    {
        EnsureRequiredReason(reason);
        EnsureAssignedReviewer(actorId);
        EnsureStatus(InspectionStatus.UnderReview);
        var from = TransitionTo(InspectionStatus.Rejected, occurredAtUtc);
        CompletedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        return from;
    }

    public InspectionStatus RequestRecheck(Guid actorId, string reason, DateTime occurredAtUtc)
    {
        EnsureRequiredReason(reason);
        EnsureAssignedReviewer(actorId);
        EnsureStatus(InspectionStatus.UnderReview);
        var from = TransitionTo(InspectionStatus.Recheck, occurredAtUtc);
        CompletedAt = null;
        return from;
    }

    public (InspectionStatus FromStatus, Guid PreviousReviewerUserId) ReassignReviewer(
        Guid reviewerUserId, string reason, DateTime occurredAtUtc)
    {
        EnsureRequiredReason(reason);
        if (Status is not (InspectionStatus.Submitted or InspectionStatus.UnderReview))
            throw InspectionRules.Error("inspection.invalid_state", "Reviewer can only be reassigned while Submitted or UnderReview.");
        if (!AssignedToUserId.HasValue)
            throw InspectionRules.Error("inspection.reviewer_missing", "The Inspection has no assigned Reviewer.");
        if (reviewerUserId == Guid.Empty || reviewerUserId == AssignedToUserId)
            throw InspectionRules.Error("inspection.reviewer_unchanged", "The new Reviewer must differ from the current Reviewer.");

        var result = (Status, AssignedToUserId.Value);
        AssignedToUserId = reviewerUserId;
        Status = InspectionStatus.Submitted;
        UpdatedAt = InspectionRules.EnsureUtc(occurredAtUtc);
        CompletedAt = null;
        return result;
    }

    private void EnsureAssignedReviewer(Guid actorId)
    {
        if (!AssignedToUserId.HasValue || AssignedToUserId != actorId)
            throw InspectionRules.Error("inspection.not_assigned_reviewer", "Only the assigned Reviewer can perform this action.");
    }

    private void EnsureStatus(InspectionStatus expected)
    {
        if (Status != expected)
            throw InspectionRules.Error("inspection.invalid_state", $"The Inspection must be {expected}.");
    }

    private InspectionStatus TransitionTo(InspectionStatus target, DateTime occurredAtUtc)
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
