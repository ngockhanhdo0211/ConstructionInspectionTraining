using ConstructionInspection.Application.Checklists.Interfaces;
using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class ChecklistReadService(ApplicationDbContext context) : IChecklistReadService
{
    public async Task<PagedResult<ChecklistListItemDto>> GetPageAsync(ChecklistQueryCriteria criteria, CancellationToken cancellationToken)
    {
        var query = Scoped(context.Checklists.AsNoTracking(), criteria.Scope);
        if (criteria.Status.HasValue)
        {
            var processStatus = criteria.Status.Value.ToProcessStatus();
            query = query.Where(x => context.BusinessProcesses.Any(p =>
                p.EntityType == ProcessEntityType.Checklist && p.ChecklistId == x.Id && p.Status == processStatus));
        }
        if (criteria.ConstructionObjectId.HasValue) query = query.Where(x => x.ConstructionObjectId == criteria.ConstructionObjectId);
        if (criteria.ReviewerUserId.HasValue) query = query.Where(x => x.ReviewerUserId == criteria.ReviewerUserId);
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(x => x.Number.Contains(search) || x.Title.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        query = Order(query, criteria.SortBy, criteria.SortDirection, context);
        var rows = await query.Skip((criteria.PageNumber - 1) * criteria.PageSize).Take(criteria.PageSize)
            .Select(x => new
            {
                Checklist = x,
                ObjectCode = context.ConstructionObjects.Where(o => o.Id == x.ConstructionObjectId).Select(o => o.Code).Single(),
                ObjectName = context.ConstructionObjects.Where(o => o.Id == x.ConstructionObjectId).Select(o => o.Name).Single(),
                Status = context.BusinessProcesses.Where(p => p.EntityType == ProcessEntityType.Checklist && p.ChecklistId == x.Id).Select(p => p.Status).Single(),
                ItemCount = context.ChecklistItems.Count(i => i.ChecklistId == x.Id),
                Pass = context.ChecklistItems.Count(i => i.ChecklistId == x.Id && i.Result == ChecklistItemResult.Pass),
                Fail = context.ChecklistItems.Count(i => i.ChecklistId == x.Id && i.Result == ChecklistItemResult.Fail),
                NA = context.ChecklistItems.Count(i => i.ChecklistId == x.Id && i.Result == ChecklistItemResult.NotApplicable),
                NC = context.ChecklistItems.Count(i => i.ChecklistId == x.Id && i.Result == ChecklistItemResult.NotChecked)
            }).ToArrayAsync(cancellationToken);
        var items = rows.Select(x => new ChecklistListItemDto(x.Checklist.Id, x.Checklist.Number, x.Checklist.Title,
            x.Checklist.ConstructionObjectId, x.ObjectCode, x.ObjectName, x.Status.ToChecklistStatus(),
            x.Checklist.CreatedByUserId, x.Checklist.ReviewerUserId, x.ItemCount, x.Pass, x.Fail, x.NA, x.NC,
            x.Checklist.CreatedAt, x.Checklist.UpdatedAt)).ToArray();
        return new(items, criteria.PageNumber, criteria.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)criteria.PageSize));
    }

    public async Task<ChecklistDetailDto?> GetDetailAsync(Guid id, ChecklistReadScope scope, CancellationToken cancellationToken)
    {
        var header = await (from checklist in Scoped(context.Checklists.AsNoTracking(), scope)
                            where checklist.Id == id
                            join target in context.ConstructionObjects.AsNoTracking() on checklist.ConstructionObjectId equals target.Id
                            join process in context.BusinessProcesses.AsNoTracking().Where(x => x.EntityType == ProcessEntityType.Checklist) on checklist.Id equals process.ChecklistId
                            select new { Checklist = checklist, target.Code, target.Name, Process = process }).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;
        var itemRows = await context.ChecklistItems.AsNoTracking().Where(x => x.ChecklistId == id)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Sequence, x.Criterion, x.IsRequired, x.Result, x.Note, x.RowVersion })
            .ToArrayAsync(cancellationToken);
        var items = itemRows.Select(x => new ChecklistItemDto(x.Id, x.Sequence, x.Criterion,
            x.IsRequired, x.Result, x.Note, Convert.ToBase64String(x.RowVersion))).ToArray();
        var links = await (from link in context.ChecklistInspections.AsNoTracking()
                           where link.ChecklistId == id
                           join inspection in context.Inspections.AsNoTracking() on link.InspectionId equals inspection.Id
                           orderby inspection.Number, inspection.Id
                           select new ChecklistInspectionDto(inspection.Id, inspection.Number, inspection.Title)).ToArrayAsync(cancellationToken);
        var c = header.Checklist;
        return new(c.Id, c.Number, c.ConstructionObjectId, header.Code, header.Name, c.Title, c.Description,
            header.Process.Status.ToChecklistStatus(), c.CreatedByUserId, c.ReviewerUserId, c.CurrentSubmissionId,
            c.NextSubmissionNumber, c.CreatedAt, c.UpdatedAt, items, links,
            Convert.ToBase64String(c.RowVersion), Convert.ToBase64String(header.Process.RowVersion));
    }

    public async Task<PagedResult<ChecklistHistoryDto>?> GetHistoryAsync(Guid id, int pageNumber, int pageSize,
        ChecklistReadScope scope, CancellationToken cancellationToken)
    {
        var processId = await Scoped(context.Checklists.AsNoTracking(), scope).Where(x => x.Id == id)
            .Select(x => context.BusinessProcesses.Where(p => p.EntityType == ProcessEntityType.Checklist && p.ChecklistId == x.Id).Select(p => (Guid?)p.Id).Single())
            .SingleOrDefaultAsync(cancellationToken);
        if (!processId.HasValue) return null;
        var query = context.ProcessHistories.AsNoTracking().Where(x => x.BusinessProcessId == processId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await (from history in query.OrderByDescending(x => x.PerformedAt).ThenByDescending(x => x.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                          join actor in context.Users.AsNoTracking() on history.PerformedByUserId equals actor.Id
                          select new { History = history, Actor = actor.FullName }).ToArrayAsync(cancellationToken);
        var items = rows.Select(x => new ChecklistHistoryDto(x.History.Id, x.History.Action,
            x.History.FromStatus?.ToChecklistStatus(),
            x.History.ToStatus.ToChecklistStatus(), x.History.PerformedByUserId, x.Actor,
            x.History.Note, x.History.PerformedAt, x.History.ChecklistSubmissionId,
            x.History.NewAssignedToUserId)).ToArray();
        return new(items, pageNumber, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<PagedResult<ChecklistSubmissionListItemDto>?> GetSubmissionsAsync(Guid id, int pageNumber,
        int pageSize, ChecklistReadScope scope, CancellationToken cancellationToken)
    {
        var current = await Scoped(context.Checklists.AsNoTracking(), scope).Where(x => x.Id == id)
            .Select(x => new { x.Id, x.CurrentSubmissionId }).SingleOrDefaultAsync(cancellationToken);
        if (current is null) return null;
        var query = context.ChecklistSubmissions.AsNoTracking().Where(x => x.ChecklistId == id);
        var total = await query.CountAsync(cancellationToken);
        var rows = await (from submission in query.OrderByDescending(x => x.SubmissionNumber).ThenByDescending(x => x.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                          join user in context.Users.AsNoTracking() on submission.SubmittedByUserId equals user.Id
                          select new ChecklistSubmissionListItemDto(submission.Id, submission.SubmissionNumber,
                              submission.SubmittedByUserId, user.FullName, submission.SubmittedAt,
                              context.ChecklistSubmissionItems.Count(x => x.ChecklistSubmissionId == submission.Id),
                              context.ChecklistSubmissionInspections.Count(x => x.ChecklistSubmissionId == submission.Id),
                              current.CurrentSubmissionId == submission.Id)).ToArrayAsync(cancellationToken);
        return new(rows, pageNumber, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<ChecklistSubmissionDetailDto?> GetSubmissionAsync(Guid id, int submissionNumber,
        ChecklistReadScope scope, CancellationToken cancellationToken)
    {
        var checklist = await Scoped(context.Checklists.AsNoTracking(), scope).Where(x => x.Id == id)
            .Select(x => new { x.Id, x.CurrentSubmissionId }).SingleOrDefaultAsync(cancellationToken);
        if (checklist is null) return null;
        var header = await (from submission in context.ChecklistSubmissions.AsNoTracking()
                            where submission.ChecklistId == id && submission.SubmissionNumber == submissionNumber
                            join user in context.Users.AsNoTracking() on submission.SubmittedByUserId equals user.Id
                            select new { Submission = submission, User = user.FullName }).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;
        var items = await context.ChecklistSubmissionItems.AsNoTracking()
            .Where(x => x.ChecklistSubmissionId == header.Submission.Id).OrderBy(x => x.Sequence).ThenBy(x => x.Id)
            .Select(x => new ChecklistSubmissionItemDto(x.SourceChecklistItemId, x.Sequence, x.Criterion,
                x.IsRequired, x.Result, x.Note)).ToArrayAsync(cancellationToken);
        var links = await (from snapshot in context.ChecklistSubmissionInspections.AsNoTracking()
                           where snapshot.ChecklistSubmissionId == header.Submission.Id
                           join inspection in context.Inspections.AsNoTracking() on snapshot.InspectionId equals inspection.Id
                           orderby inspection.Number, inspection.Id
                           select new ChecklistInspectionDto(inspection.Id, inspection.Number, inspection.Title)).ToArrayAsync(cancellationToken);
        var s = header.Submission;
        return new(s.Id, s.SubmissionNumber, s.SubmittedByUserId, header.User, s.SubmittedAt,
            items, links, checklist.CurrentSubmissionId == s.Id);
    }

    private static IQueryable<Checklist> Scoped(IQueryable<Checklist> query, ChecklistReadScope scope)
    {
        if (scope.CanReadAll) return query;
        if (scope.IsInspector && scope.IsReviewer)
            return query.Where(x => x.CreatedByUserId == scope.UserId || x.ReviewerUserId == scope.UserId);
        if (scope.IsInspector) return query.Where(x => x.CreatedByUserId == scope.UserId);
        return query.Where(x => x.ReviewerUserId == scope.UserId);
    }

    private static IQueryable<Checklist> Order(IQueryable<Checklist> query, string sortBy, string direction,
        ApplicationDbContext context)
    {
        var desc = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "number" => desc ? query.OrderByDescending(x => x.Number).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Number).ThenBy(x => x.Id),
            "title" => desc ? query.OrderByDescending(x => x.Title).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Title).ThenBy(x => x.Id),
            "status" => desc
                ? query.OrderByDescending(x => context.BusinessProcesses.Where(p => p.EntityType == ProcessEntityType.Checklist && p.ChecklistId == x.Id).Select(p => p.Status).Single()).ThenByDescending(x => x.Id)
                : query.OrderBy(x => context.BusinessProcesses.Where(p => p.EntityType == ProcessEntityType.Checklist && p.ChecklistId == x.Id).Select(p => p.Status).Single()).ThenBy(x => x.Id),
            "updatedat" => desc ? query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        };
    }
}
