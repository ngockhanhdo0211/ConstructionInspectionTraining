namespace ConstructionInspection.Domain.Inspections;

public sealed class InspectionSubmission
{
    private InspectionSubmission() { }

    public Guid Id { get; private set; }
    public Guid InspectionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string DataJson { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static InspectionSubmission CreateInitial(
        Guid id, Guid inspectionId, string dataJson, Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || inspectionId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "Submission, Inspection and actor identifiers are required.");
        EnsureData(dataJson);
        return new InspectionSubmission
        {
            Id = id,
            InspectionId = inspectionId,
            RevisionNumber = 1,
            DataJson = dataJson,
            CreatedByUserId = actorId,
            CreatedAt = InspectionRules.EnsureUtc(createdAtUtc)
        };
    }

    public static InspectionSubmission CreateRevision(
        Guid id, Guid inspectionId, int revisionNumber, string dataJson,
        Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || inspectionId == Guid.Empty || actorId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "Submission, Inspection and actor identifiers are required.");
        if (revisionNumber < 2)
            throw InspectionRules.Error("inspection.invalid_revision", "A new revision number must be at least 2.");
        EnsureData(dataJson);
        return new InspectionSubmission
        {
            Id = id,
            InspectionId = inspectionId,
            RevisionNumber = revisionNumber,
            DataJson = dataJson,
            CreatedByUserId = actorId,
            CreatedAt = InspectionRules.EnsureUtc(createdAtUtc)
        };
    }

    public void UpdateDraft(string dataJson, Guid actorId, DateTime updatedAtUtc)
    {
        if (SubmittedAt.HasValue)
            throw InspectionRules.Error("inspection.revision_immutable", "A submitted revision is immutable.");
        if (actorId != CreatedByUserId)
            throw InspectionRules.Error("inspection.not_owner", "Only the creator can update this revision.");
        EnsureData(dataJson);
        DataJson = dataJson;
        UpdatedAt = InspectionRules.EnsureUtc(updatedAtUtc);
    }

    public void Submit(DateTime submittedAtUtc)
    {
        if (SubmittedAt.HasValue)
            throw InspectionRules.Error("inspection.revision_immutable", "A submitted revision is immutable.");
        SubmittedAt = InspectionRules.EnsureUtc(submittedAtUtc);
        UpdatedAt = SubmittedAt;
    }

    private static void EnsureData(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            throw InspectionRules.Error("inspection.form_data_invalid", "DataJson is required.");
    }
}
