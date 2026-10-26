using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Rfis.Interfaces;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Rfis;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class RfiReadService(ApplicationDbContext context) : IRfiReadService
{
    public async Task<PagedResult<RfiListItemDto>> GetPageAsync(RfiQueryCriteria c, CancellationToken ct)
    {
        var query = Scoped(context.Rfis.AsNoTracking(), c.Scope);
        if (c.Status.HasValue)
        {
            var status = c.Status.Value.ToProcessStatus();
            query = query.Where(x => context.BusinessProcesses.Any(p => p.RfiId == x.Id && p.EntityType == ProcessEntityType.Rfi && p.Status == status));
        }
        if (c.ConstructionObjectId.HasValue) query = query.Where(x => x.ConstructionObjectId == c.ConstructionObjectId);
        if (c.AssignedToMe) query = query.Where(x => context.BusinessProcesses.Any(p => p.RfiId == x.Id && p.AssignedToUserId == c.Scope.UserId));
        if (c.Overdue.HasValue)
        {
            query = c.Overdue.Value
                ? query.Where(x => context.BusinessProcesses.Any(p => p.RfiId == x.Id && p.Status == ProcessStatus.Open && p.DueAt < c.UtcNow))
                : query.Where(x => !context.BusinessProcesses.Any(p => p.RfiId == x.Id && p.Status == ProcessStatus.Open && p.DueAt < c.UtcNow));
        }
        if (!string.IsNullOrWhiteSpace(c.Search))
        {
            var search = c.Search.Trim();
            query = query.Where(x => x.Number.Contains(search) || x.Subject.Contains(search));
        }
        var total = await query.CountAsync(ct);
        query = Order(query, c.SortBy, c.SortDirection);
        var rows = await (from rfi in query.Skip((c.PageNumber - 1) * c.PageSize).Take(c.PageSize)
                          join target in context.ConstructionObjects.AsNoTracking() on rfi.ConstructionObjectId equals target.Id
                          join process in context.BusinessProcesses.AsNoTracking().Where(x => x.EntityType == ProcessEntityType.Rfi) on rfi.Id equals process.RfiId
                          join reviewer in context.Users.AsNoTracking() on process.AssignedToUserId equals reviewer.Id into reviewers
                          from reviewer in reviewers.DefaultIfEmpty()
                          select new { Rfi = rfi, target.Code, target.Name, Process = process, ReviewerName = reviewer == null ? null : reviewer.FullName }).ToArrayAsync(ct);
        var items = rows.Select(x => new RfiListItemDto(x.Rfi.Id, x.Rfi.Number, x.Rfi.Subject,
            x.Rfi.ConstructionObjectId, x.Code, x.Name, x.Process.Status.ToRfiStatus(), x.Rfi.CreatedByUserId,
            x.Process.AssignedToUserId, x.ReviewerName, x.Process.DueAt,
            x.Process.Status == ProcessStatus.Open && x.Process.DueAt < c.UtcNow,
            x.Rfi.CreatedAt, x.Rfi.UpdatedAt, Convert.ToBase64String(x.Rfi.RowVersion),
            Convert.ToBase64String(x.Process.RowVersion))).ToArray();
        return new(items, c.PageNumber, c.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)c.PageSize));
    }

    public async Task<RfiDetailDto?> GetDetailAsync(Guid id, RfiReadScope scope, CancellationToken ct)
    {
        var header = await (from rfi in Scoped(context.Rfis.AsNoTracking(), scope)
                            where rfi.Id == id
                            join target in context.ConstructionObjects.AsNoTracking() on rfi.ConstructionObjectId equals target.Id
                            join process in context.BusinessProcesses.AsNoTracking().Where(x => x.EntityType == ProcessEntityType.Rfi) on rfi.Id equals process.RfiId
                            join reviewer in context.Users.AsNoTracking() on process.AssignedToUserId equals reviewer.Id into reviewers
                            from reviewer in reviewers.DefaultIfEmpty()
                            select new { Rfi = rfi, target.Code, target.Name, Process = process, ReviewerName = reviewer == null ? null : reviewer.FullName }).SingleOrDefaultAsync(ct);
        if (header is null) return null;
        var entries = await (from entry in context.RfiEntries.AsNoTracking()
                             where entry.RfiId == id
                             join actor in context.Users.AsNoTracking() on entry.CreatedByUserId equals actor.Id
                             orderby entry.EntryNumber, entry.Id
                             select new RfiEntryDto(entry.Id, entry.EntryNumber, entry.EntryType, entry.Content,
                                 entry.CreatedByUserId, actor.FullName, entry.CreatedAtUtc)).ToArrayAsync(ct);
        var r = header.Rfi;
        return new(r.Id, r.Number, r.ConstructionObjectId, header.Code, header.Name, r.Subject, r.Question,
            header.Process.Status.ToRfiStatus(), r.CreatedByUserId, header.Process.AssignedToUserId,
            header.ReviewerName, header.Process.DueAt, header.Process.CompletedAt, r.CreatedAt, r.UpdatedAt,
            entries, Convert.ToBase64String(r.RowVersion), Convert.ToBase64String(header.Process.RowVersion));
    }

    public async Task<PagedResult<RfiHistoryDto>?> GetHistoryAsync(Guid id, int pageNumber, int pageSize,
        RfiReadScope scope, CancellationToken ct)
    {
        var processId = await Scoped(context.Rfis.AsNoTracking(), scope).Where(x => x.Id == id)
            .Select(x => context.BusinessProcesses.Where(p => p.RfiId == x.Id && p.EntityType == ProcessEntityType.Rfi)
                .Select(p => (Guid?)p.Id).Single()).SingleOrDefaultAsync(ct);
        if (!processId.HasValue) return null;
        var query = context.ProcessHistories.AsNoTracking().Where(x => x.BusinessProcessId == processId);
        var total = await query.CountAsync(ct);
        var rows = await (from h in query.OrderByDescending(x => x.PerformedAt).ThenByDescending(x => x.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                          join actor in context.Users.AsNoTracking() on h.PerformedByUserId equals actor.Id
                          select new { H = h, Actor = actor.FullName }).ToArrayAsync(ct);
        var items = rows.Select(x => new RfiHistoryDto(x.H.Id, x.H.Action,
            x.H.FromStatus?.ToRfiStatus(), x.H.ToStatus.ToRfiStatus(), x.H.PerformedByUserId,
            x.Actor, x.H.Note, x.H.PerformedAt, x.H.RfiEntryId,
            x.H.PreviousAssignedToUserId, x.H.NewAssignedToUserId)).ToArray();
        return new(items, pageNumber, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    private IQueryable<Rfi> Scoped(IQueryable<Rfi> query, RfiReadScope scope)
    {
        if (scope.CanReadAll) return query;
        if (scope.IsInspector && scope.IsReviewer)
            return query.Where(x => x.CreatedByUserId == scope.UserId || context.BusinessProcesses.Any(p => p.RfiId == x.Id && p.AssignedToUserId == scope.UserId));
        if (scope.IsInspector) return query.Where(x => x.CreatedByUserId == scope.UserId);
        return query.Where(x => context.BusinessProcesses.Any(p => p.RfiId == x.Id && p.AssignedToUserId == scope.UserId));
    }

    private IQueryable<Rfi> Order(IQueryable<Rfi> query, string sortBy, string direction)
    {
        var desc = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "number" => desc ? query.OrderByDescending(x => x.Number).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Number).ThenBy(x => x.Id),
            "subject" => desc ? query.OrderByDescending(x => x.Subject).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Subject).ThenBy(x => x.Id),
            "updatedat" => desc ? query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
            "status" => desc ? query.OrderByDescending(x => context.BusinessProcesses.Where(p => p.RfiId == x.Id).Select(p => p.Status).Single()).ThenByDescending(x => x.Id) : query.OrderBy(x => context.BusinessProcesses.Where(p => p.RfiId == x.Id).Select(p => p.Status).Single()).ThenBy(x => x.Id),
            "dueatutc" => desc ? query.OrderByDescending(x => context.BusinessProcesses.Where(p => p.RfiId == x.Id).Select(p => p.DueAt).Single()).ThenByDescending(x => x.Id) : query.OrderBy(x => context.BusinessProcesses.Where(p => p.RfiId == x.Id).Select(p => p.DueAt).Single()).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        };
    }
}
