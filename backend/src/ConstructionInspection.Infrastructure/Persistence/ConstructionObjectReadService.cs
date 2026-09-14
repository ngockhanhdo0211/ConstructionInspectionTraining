using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class ConstructionObjectReadService(ApplicationDbContext context)
    : IConstructionObjectReadService
{
    public async Task<PagedResult<ConstructionObjectListItemDto>> GetPageAsync(
        ConstructionObjectQueryCriteria criteria,
        CancellationToken cancellationToken)
    {
        IQueryable<ConstructionObject> query = context.ConstructionObjects.AsNoTracking();
        if (!criteria.IncludeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        if (criteria.IsActive.HasValue)
        {
            query = query.Where(item => item.IsActive == criteria.IsActive.Value);
        }

        if (criteria.ObjectType.HasValue)
        {
            query = query.Where(item => item.ObjectType == criteria.ObjectType.Value);
        }

        query = criteria.ParentFilterMode switch
        {
            ParentFilterMode.Roots => query.Where(item => item.ParentId == null),
            ParentFilterMode.Specific => query.Where(item => item.ParentId == criteria.ParentId),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(item => item.Code.Contains(search)
                || item.Name.Contains(search)
                || (item.Location != null && item.Location.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, criteria.SortBy, criteria.SortDirection);
        var projections = await query
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(item => new ListProjection(
                item.Id,
                item.ParentId,
                item.Code,
                item.Name,
                item.ObjectType,
                item.Location,
                item.IsActive,
                item.RowVersion))
            .ToArrayAsync(cancellationToken);
        var items = projections
            .Select(item => new ConstructionObjectListItemDto(
                item.Id,
                item.ParentId,
                item.Code,
                item.Name,
                item.ObjectType,
                item.Location,
                item.IsActive,
                Convert.ToBase64String(item.RowVersion)))
            .ToArray();

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)criteria.PageSize);
        return new PagedResult<ConstructionObjectListItemDto>(
            items,
            criteria.PageNumber,
            criteria.PageSize,
            totalCount,
            totalPages);
    }

    public async Task<ConstructionObjectDetailDto?> GetDetailAsync(
        Guid id,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var item = await context.ConstructionObjects.AsNoTracking()
            .Where(value => value.Id == id && (includeInactive || value.IsActive))
            .Select(value => new DetailProjection(
                value.Id,
                value.ParentId,
                value.Code,
                value.Name,
                value.ObjectType,
                value.Location,
                value.Description,
                value.IsActive,
                value.CreatedByUserId,
                value.CreatedAt,
                value.UpdatedByUserId,
                value.UpdatedAt,
                value.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return null;
        }

        var directChildrenCount = await context.ConstructionObjects.AsNoTracking()
            .CountAsync(child => child.ParentId == id && (includeInactive || child.IsActive), cancellationToken);
        var breadcrumb = await GetBreadcrumbAsync(item.Id, includeInactive, cancellationToken);
        var parent = item.ParentId.HasValue
            ? breadcrumb.Count > 1 ? breadcrumb[^2] : null
            : null;

        return new ConstructionObjectDetailDto(
            item.Id,
            item.ParentId,
            item.Code,
            item.Name,
            item.ObjectType,
            item.Location,
            item.Description,
            item.IsActive,
            parent,
            breadcrumb,
            directChildrenCount > 0,
            directChildrenCount,
            item.CreatedByUserId,
            item.CreatedAt,
            item.UpdatedByUserId,
            item.UpdatedAt,
            Convert.ToBase64String(item.RowVersion));
    }

    private async Task<IReadOnlyList<ConstructionObjectBreadcrumbItemDto>> GetBreadcrumbAsync(
        Guid id,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var result = new List<ConstructionObjectBreadcrumbItemDto>();
        var visited = new HashSet<Guid>();
        Guid? currentId = id;

        for (var iteration = 0;
             currentId.HasValue && iteration < ConstructionObjectRules.HierarchyTraversalSafetyLimit;
             iteration++)
        {
            if (!visited.Add(currentId.Value))
            {
                throw new InvalidOperationException("Construction Object breadcrumb contains a cycle.");
            }

            var node = await context.ConstructionObjects.AsNoTracking()
                .Where(item => item.Id == currentId.Value && (includeInactive || item.IsActive))
                .Select(item => new BreadcrumbProjection(
                    item.Id,
                    item.ParentId,
                    item.Code,
                    item.Name,
                    item.ObjectType))
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Construction Object breadcrumb is incomplete.");

            result.Add(new ConstructionObjectBreadcrumbItemDto(
                node.Id,
                node.Code,
                node.Name,
                node.ObjectType));
            currentId = node.ParentId;
        }

        if (currentId.HasValue)
        {
            throw new InvalidOperationException("Construction Object breadcrumb exceeded its safety limit.");
        }

        result.Reverse();
        return result;
    }

    private static IQueryable<ConstructionObject> ApplyOrdering(
        IQueryable<ConstructionObject> query,
        string sortBy,
        string direction)
    {
        var descending = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "code" => descending
                ? query.OrderByDescending(item => item.Code).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.Code).ThenBy(item => item.Id),
            "objecttype" => descending
                ? query.OrderByDescending(item => item.ObjectType).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.ObjectType).ThenBy(item => item.Id),
            "createdat" => descending
                ? query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
            "updatedat" => descending
                ? query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.Id),
            _ => descending
                ? query.OrderByDescending(item => item.Name).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.Name).ThenBy(item => item.Id)
        };
    }

    private sealed record DetailProjection(
        Guid Id,
        Guid? ParentId,
        string Code,
        string Name,
        ConstructionObjectType ObjectType,
        string? Location,
        string? Description,
        bool IsActive,
        Guid CreatedByUserId,
        DateTime CreatedAt,
        Guid? UpdatedByUserId,
        DateTime? UpdatedAt,
        byte[] RowVersion);

    private sealed record ListProjection(
        Guid Id,
        Guid? ParentId,
        string Code,
        string Name,
        ConstructionObjectType ObjectType,
        string? Location,
        bool IsActive,
        byte[] RowVersion);

    private sealed record BreadcrumbProjection(
        Guid Id,
        Guid? ParentId,
        string Code,
        string Name,
        ConstructionObjectType ObjectType);
}
