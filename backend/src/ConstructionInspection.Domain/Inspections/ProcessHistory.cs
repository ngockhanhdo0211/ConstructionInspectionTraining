namespace ConstructionInspection.Domain.Inspections;

public sealed class ProcessHistory
{
    private ProcessHistory() { }

    public Guid Id { get; private set; }
    public Guid BusinessProcessId { get; private set; }
    public InspectionStatus? FromStatus { get; private set; }
    public InspectionStatus ToStatus { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public Guid PerformedByUserId { get; private set; }
    public DateTime PerformedAt { get; private set; }
    public Guid? SubmissionId { get; private set; }
    public Guid? PreviousAssignedToUserId { get; private set; }
    public Guid? NewAssignedToUserId { get; private set; }

    public static ProcessHistory CreateInitial(
        Guid id, Guid businessProcessId, Guid submissionId, Guid actorId, DateTime performedAtUtc)
    {
        if (id == Guid.Empty || businessProcessId == Guid.Empty
            || submissionId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "History, process, submission and actor identifiers are required.");
        return new ProcessHistory
        {
            Id = id,
            BusinessProcessId = businessProcessId,
            FromStatus = null,
            ToStatus = InspectionStatus.Draft,
            Action = "Create",
            PerformedByUserId = actorId,
            PerformedAt = InspectionRules.EnsureUtc(performedAtUtc),
            SubmissionId = submissionId
        };
    }

    public static ProcessHistory Create(
        Guid id,
        Guid businessProcessId,
        InspectionStatus fromStatus,
        InspectionStatus toStatus,
        string action,
        Guid actorId,
        DateTime performedAtUtc,
        Guid submissionId,
        string? note = null,
        Guid? previousAssignedToUserId = null,
        Guid? newAssignedToUserId = null)
    {
        if (id == Guid.Empty || businessProcessId == Guid.Empty
            || submissionId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "History, process, submission and actor identifiers are required.");
        if (!Actions.Contains(action, StringComparer.Ordinal))
            throw InspectionRules.Error("inspection.history_action_invalid", "Process History action is invalid.");
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote?.Length > InspectionRules.MaximumWorkflowNoteLength)
            throw InspectionRules.Error("inspection.note_invalid", "Workflow note exceeds the allowed length.");
        if (action == "ReassignReviewer"
            && (!previousAssignedToUserId.HasValue || !newAssignedToUserId.HasValue))
            throw InspectionRules.Error("inspection.reviewer_audit_invalid", "Reviewer reassignment audit is required.");

        return new ProcessHistory
        {
            Id = id,
            BusinessProcessId = businessProcessId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Action = action,
            Note = normalizedNote,
            PerformedByUserId = actorId,
            PerformedAt = InspectionRules.EnsureUtc(performedAtUtc),
            SubmissionId = submissionId,
            PreviousAssignedToUserId = previousAssignedToUserId,
            NewAssignedToUserId = newAssignedToUserId
        };
    }

    private static readonly string[] Actions =
    [
        "Submit", "StartReview", "Approve", "Reject", "RequestRecheck",
        "ReassignReviewer", "CreateRevision"
    ];
}
