using ConstructionInspection.Application.Checklists.Interfaces;
using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class ChecklistRepository(ApplicationDbContext context) : IChecklistRepository
{
    public async Task<ChecklistObjectState?> GetObjectForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var value = await context.ConstructionObjects
            .FromSqlInterpolated($"SELECT * FROM [ConstructionObjects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().Select(x => new { x.Id, x.IsActive }).SingleOrDefaultAsync(cancellationToken);
        return value is null ? null : new(value.Id, value.IsActive);
    }

    public async Task<ChecklistWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var checklist = await context.Checklists
            .FromSqlInterpolated($"SELECT * FROM [Checklists] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        if (checklist is null) return null;
        var process = await context.BusinessProcesses
            .FromSqlInterpolated($"SELECT * FROM [BusinessProcesses] WITH (UPDLOCK, HOLDLOCK) WHERE [ChecklistId] = {id} AND [EntityType] = 'Checklist'")
            .SingleOrDefaultAsync(cancellationToken);
        if (process is null || process.EntityType != ProcessEntityType.Checklist) return null;
        var items = await context.ChecklistItems
            .FromSqlInterpolated($"SELECT * FROM [ChecklistItems] WITH (UPDLOCK, HOLDLOCK) WHERE [ChecklistId] = {id}")
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var links = await context.ChecklistInspections
            .FromSqlInterpolated($"SELECT * FROM [ChecklistInspections] WITH (UPDLOCK, HOLDLOCK) WHERE [ChecklistId] = {id}")
            .OrderBy(x => x.InspectionId).ToListAsync(cancellationToken);
        return new(checklist, items, links, process);
    }

    public async Task<IReadOnlyList<ChecklistInspectionState>> GetInspectionsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];
        return await context.Inspections.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id)
            .Select(x => new ChecklistInspectionState(x.Id, x.ConstructionObjectId, x.CreatedByUserId))
            .ToArrayAsync(cancellationToken);
    }

    public void AddChecklist(Checklist checklist) => context.Checklists.Add(checklist);
    public void AddItems(IEnumerable<ChecklistItem> items) => context.ChecklistItems.AddRange(items);
    public void RemoveItems(IEnumerable<ChecklistItem> items) => context.ChecklistItems.RemoveRange(items);
    public void ReplaceLinks(IEnumerable<ChecklistInspection> current, IEnumerable<ChecklistInspection> replacement)
    {
        context.ChecklistInspections.RemoveRange(current);
        context.ChecklistInspections.AddRange(replacement);
    }
    public void AddFoundation(BusinessProcess process, ProcessHistory history)
    {
        context.BusinessProcesses.Add(process);
        context.ProcessHistories.Add(history);
    }
    public void AddSubmission(ChecklistSubmission submission, IEnumerable<ChecklistSubmissionItem> items,
        IEnumerable<ChecklistSubmissionInspection> inspections)
    {
        context.ChecklistSubmissions.Add(submission);
        context.ChecklistSubmissionItems.AddRange(items);
        context.ChecklistSubmissionInspections.AddRange(inspections);
    }
    public void AddHistory(ProcessHistory history) => context.ProcessHistories.Add(history);
}
