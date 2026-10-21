using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.Checklists.Interfaces;

public sealed record ChecklistObjectState(Guid Id, bool IsActive);
public sealed record ChecklistInspectionState(Guid Id, Guid ConstructionObjectId, Guid CreatedByUserId);
public sealed record ChecklistWriteAggregate(Checklist Checklist, List<ChecklistItem> Items,
    List<ChecklistInspection> Links, BusinessProcess Process);

public interface IChecklistRepository
{
    Task<ChecklistObjectState?> GetObjectForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<ChecklistWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChecklistInspectionState>> GetInspectionsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void AddChecklist(Checklist checklist);
    void AddItems(IEnumerable<ChecklistItem> items);
    void RemoveItems(IEnumerable<ChecklistItem> items);
    void ReplaceLinks(IEnumerable<ChecklistInspection> current, IEnumerable<ChecklistInspection> replacement);
    void AddFoundation(BusinessProcess process, ProcessHistory history);
    void AddSubmission(ChecklistSubmission submission,
        IEnumerable<ChecklistSubmissionItem> items,
        IEnumerable<ChecklistSubmissionInspection> inspections);
    void AddHistory(ProcessHistory history);
}
