namespace ConstructionInspection.Domain.Checklists;

public sealed class ChecklistSubmission
{
    private ChecklistSubmission() { }
    public Guid Id { get; private set; }
    public Guid ChecklistId { get; private set; }
    public int SubmissionNumber { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    public DateTime SubmittedAt { get; private set; }

    public static ChecklistSubmission Create(Guid id, Guid checklistId, int submissionNumber,
        Guid actorId, DateTime submittedAtUtc)
    {
        if (id == Guid.Empty || checklistId == Guid.Empty || actorId == Guid.Empty || submissionNumber <= 0)
            throw ChecklistRules.Error("checklist.submission_invalid", "Submission identifiers and number are required.");
        return new()
        {
            Id = id,
            ChecklistId = checklistId,
            SubmissionNumber = submissionNumber,
            SubmittedByUserId = actorId,
            SubmittedAt = ChecklistRules.EnsureUtc(submittedAtUtc)
        };
    }
}

public sealed class ChecklistSubmissionItem
{
    private ChecklistSubmissionItem() { }
    public Guid Id { get; private set; }
    public Guid ChecklistSubmissionId { get; private set; }
    public Guid SourceChecklistItemId { get; private set; }
    public int Sequence { get; private set; }
    public string Criterion { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }
    public ChecklistItemResult Result { get; private set; }
    public string? Note { get; private set; }

    public static ChecklistSubmissionItem Capture(Guid id, Guid submissionId, ChecklistItem source)
    {
        source.ValidateForSubmit();
        if (id == Guid.Empty || submissionId == Guid.Empty)
            throw ChecklistRules.Error("checklist.snapshot_invalid", "Snapshot identifiers are required.");
        return new()
        {
            Id = id,
            ChecklistSubmissionId = submissionId,
            SourceChecklistItemId = source.Id,
            Sequence = source.Sequence,
            Criterion = source.Criterion,
            IsRequired = source.IsRequired,
            Result = source.Result,
            Note = source.Note
        };
    }
}

public sealed class ChecklistSubmissionInspection
{
    private ChecklistSubmissionInspection() { }
    public Guid ChecklistSubmissionId { get; private set; }
    public Guid InspectionId { get; private set; }

    public static ChecklistSubmissionInspection Capture(Guid submissionId, Guid inspectionId)
    {
        if (submissionId == Guid.Empty || inspectionId == Guid.Empty)
            throw ChecklistRules.Error("checklist.snapshot_link_invalid", "Snapshot and Inspection identifiers are required.");
        return new() { ChecklistSubmissionId = submissionId, InspectionId = inspectionId };
    }
}
