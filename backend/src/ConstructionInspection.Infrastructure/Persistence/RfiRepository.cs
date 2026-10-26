using ConstructionInspection.Application.Rfis.Interfaces;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Rfis;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class RfiRepository(ApplicationDbContext context) : IRfiRepository
{
    public async Task<RfiObjectState?> GetObjectForUpdateAsync(Guid id, CancellationToken ct)
    {
        var row = await context.ConstructionObjects
            .FromSqlInterpolated($"SELECT * FROM [ConstructionObjects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().Select(x => new { x.Id, x.IsActive }).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.Id, row.IsActive);
    }

    public async Task<RfiWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken ct)
    {
        var rfi = await context.Rfis
            .FromSqlInterpolated($"SELECT * FROM [Rfis] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(ct);
        if (rfi is null) return null;
        var process = await context.BusinessProcesses
            .FromSqlInterpolated($"SELECT * FROM [BusinessProcesses] WITH (UPDLOCK, HOLDLOCK) WHERE [RfiId] = {id} AND [EntityType] = 'Rfi'")
            .SingleOrDefaultAsync(ct);
        if (process is null || process.EntityType != ProcessEntityType.Rfi) return null;
        var entries = await context.RfiEntries
            .FromSqlInterpolated($"SELECT * FROM [RfiEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [RfiId] = {id}")
            .OrderBy(x => x.EntryNumber).ThenBy(x => x.Id).ToListAsync(ct);
        return new(rfi, process, entries);
    }
    public void AddRfi(Rfi rfi) => context.Rfis.Add(rfi);
    public void AddEntry(RfiEntry entry) => context.RfiEntries.Add(entry);
    public void AddFoundation(BusinessProcess process, ProcessHistory history)
    { context.BusinessProcesses.Add(process); context.ProcessHistories.Add(history); }
    public void AddHistory(ProcessHistory history) => context.ProcessHistories.Add(history);
}
