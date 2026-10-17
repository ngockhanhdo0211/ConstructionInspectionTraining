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
}
