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
}
