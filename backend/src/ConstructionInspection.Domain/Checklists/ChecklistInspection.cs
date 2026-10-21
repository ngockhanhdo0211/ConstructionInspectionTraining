namespace ConstructionInspection.Domain.Checklists;

public sealed class ChecklistInspection
{
    private ChecklistInspection() { }
    public Guid ChecklistId { get; private set; }
    public Guid InspectionId { get; private set; }
    public Guid LinkedByUserId { get; private set; }
    public DateTime LinkedAt { get; private set; }

    public static ChecklistInspection Create(Guid checklistId, Guid inspectionId, Guid actorId, DateTime linkedAtUtc)
    {
        if (checklistId == Guid.Empty || inspectionId == Guid.Empty || actorId == Guid.Empty)
            throw ChecklistRules.Error("checklist.link_invalid", "Checklist, Inspection and actor identifiers are required.");
        return new()
        {
            ChecklistId = checklistId,
            InspectionId = inspectionId,
            LinkedByUserId = actorId,
            LinkedAt = ChecklistRules.EnsureUtc(linkedAtUtc)
        };
    }
}
