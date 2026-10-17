using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Domain.Inspections;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class InspectionReadService(ApplicationDbContext context) : IInspectionReadService
{
    public async Task<PagedResult<InspectionListItemDto>> GetPageAsync(
        InspectionQueryCriteria criteria, CancellationToken cancellationToken)
    {
        var query = Scoped(context.Inspections.AsNoTracking(), criteria.Scope);
        if (criteria.Status.HasValue)
            query = query.Where(x => context.BusinessProcesses.Any(p => p.InspectionId == x.Id && p.Status == criteria.Status));
        if (criteria.ConstructionObjectId.HasValue)
            query = query.Where(x => x.ConstructionObjectId == criteria.ConstructionObjectId);
        if (criteria.ReviewerUserId.HasValue)
            query = query.Where(x => x.ReviewerUserId == criteria.ReviewerUserId);
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(x => x.Number.Contains(search) || x.Title.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        query = Order(query, criteria.SortBy, criteria.SortDirection);
        var rows = await query.Skip((criteria.PageNumber - 1) * criteria.PageSize).Take(criteria.PageSize)
            .Select(x => new
            {
                x.Id,
                x.Number,
                x.Title,
                x.ConstructionObjectId,
                x.FormTemplateVersionId,
                VersionNumber = context.FormTemplateVersions.Where(v => v.Id == x.FormTemplateVersionId).Select(v => v.VersionNumber).Single(),
                Status = context.BusinessProcesses.Where(p => p.InspectionId == x.Id).Select(p => p.Status).Single(),
                x.CreatedByUserId,
                x.ReviewerUserId,
                x.CreatedAt,
                x.UpdatedAt,
                x.RowVersion
            }).ToArrayAsync(cancellationToken);
        var items = rows.Select(x => new InspectionListItemDto(
            x.Id, x.Number, x.Title, x.ConstructionObjectId, x.FormTemplateVersionId,
            x.VersionNumber, x.Status, x.CreatedByUserId, x.ReviewerUserId,
            x.CreatedAt, x.UpdatedAt, Convert.ToBase64String(x.RowVersion))).ToArray();
        return new(items, criteria.PageNumber, criteria.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)criteria.PageSize));
    }

    public async Task<InspectionDetailDto?> GetDetailAsync(
        Guid id, InspectionReadScope scope, CancellationToken cancellationToken)
    {
        var row = await (
            from inspection in Scoped(context.Inspections.AsNoTracking(), scope)
            where inspection.Id == id
            join target in context.ConstructionObjects.AsNoTracking() on inspection.ConstructionObjectId equals target.Id
            join version in context.FormTemplateVersions.AsNoTracking() on inspection.FormTemplateVersionId equals version.Id
            join template in context.FormTemplates.AsNoTracking() on version.FormTemplateId equals template.Id
            join submission in context.InspectionSubmissions.AsNoTracking() on inspection.CurrentSubmissionId equals submission.Id
            join process in context.BusinessProcesses.AsNoTracking() on inspection.Id equals process.InspectionId
            where submission.InspectionId == inspection.Id
            select new
            {
                Inspection = inspection,
                ObjectCode = target.Code,
                ObjectName = target.Name,
                TemplateId = template.Id,
                template.Code,
                template.Name,
                VersionId = version.Id,
                version.VersionNumber,
                version.SchemaJson,
                Submission = submission,
                Process = process
            }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        return new(
            row.Inspection.Id, row.Inspection.Number, row.Inspection.ConstructionObjectId,
            row.ObjectCode, row.ObjectName, row.Inspection.Title, row.Inspection.Description,
            new(row.TemplateId, row.Code, row.Name, row.VersionId, row.VersionNumber, row.SchemaJson),
            row.Process.Status, row.Inspection.CreatedByUserId, row.Inspection.ReviewerUserId,
            row.Inspection.CreatedAt, row.Inspection.UpdatedAt,
            new(row.Submission.Id, row.Submission.RevisionNumber, row.Submission.DataJson,
                row.Submission.CreatedAt, row.Submission.UpdatedAt, row.Submission.SubmittedAt,
                Convert.ToBase64String(row.Submission.RowVersion)),
            Convert.ToBase64String(row.Inspection.RowVersion), Convert.ToBase64String(row.Process.RowVersion));
    }

    private static IQueryable<Inspection> Scoped(IQueryable<Inspection> query, InspectionReadScope scope)
    {
        if (scope.CanReadAll) return query;
        return query.Where(x => (scope.IsInspector && x.CreatedByUserId == scope.UserId)
            || (scope.IsReviewer && x.ReviewerUserId == scope.UserId));
    }

    private IQueryable<Inspection> Order(IQueryable<Inspection> query, string field, string direction)
    {
        var desc = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return field.ToLowerInvariant() switch
        {
            "number" => desc ? query.OrderByDescending(x => x.Number).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Number).ThenBy(x => x.Id),
            "title" => desc ? query.OrderByDescending(x => x.Title).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Title).ThenBy(x => x.Id),
            "status" => desc
                ? query.OrderByDescending(x => context.BusinessProcesses.Where(p => p.InspectionId == x.Id).Select(p => p.Status).Single()).ThenByDescending(x => x.Id)
                : query.OrderBy(x => context.BusinessProcesses.Where(p => p.InspectionId == x.Id).Select(p => p.Status).Single()).ThenBy(x => x.Id),
            "updatedat" => desc ? query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        };
    }
}
