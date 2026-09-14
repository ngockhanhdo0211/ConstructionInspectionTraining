using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class ConstructionObjectRepository(ApplicationDbContext context)
    : IConstructionObjectRepository
{
    public Task<ConstructionObject?> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken) => context.ConstructionObjects
            .FromSqlInterpolated($"SELECT * FROM [ConstructionObjects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ConstructionObjectHierarchyNode?> GetHierarchyNodeAsync(
        Guid id,
        CancellationToken cancellationToken) => context.ConstructionObjects
            .FromSqlInterpolated($"SELECT * FROM [ConstructionObjects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking()
            .Select(item => new ConstructionObjectHierarchyNode(
                item.Id,
                item.ParentId,
                item.ObjectType,
                item.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(
        string normalizedCode,
        Guid? excludingId,
        CancellationToken cancellationToken) => context.ConstructionObjects
            .AnyAsync(item => item.Code == normalizedCode
                && (!excludingId.HasValue || item.Id != excludingId.Value), cancellationToken);

    public async Task<bool> HasActiveDescendantsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { id };
        IReadOnlyCollection<Guid> frontier = [id];

        for (var iteration = 0;
             iteration < ConstructionObjectRules.HierarchyTraversalSafetyLimit;
             iteration++)
        {
            var parentIds = frontier.ToArray();
            var descendants = await context.ConstructionObjects
                .AsNoTracking()
                .Where(item => item.ParentId.HasValue
                    && parentIds.Contains(item.ParentId.Value))
                .Select(item => new { item.Id, item.IsActive })
                .ToArrayAsync(cancellationToken);

            if (descendants.Any(item => item.IsActive))
            {
                return true;
            }

            if (descendants.Length == 0)
            {
                return false;
            }

            var next = new List<Guid>(descendants.Length);
            foreach (var descendant in descendants)
            {
                if (!visited.Add(descendant.Id))
                {
                    throw new ConstructionInspection.Application.Common.Exceptions.ConflictException(
                        "construction_object.tree_invalid",
                        "The existing Construction Object hierarchy contains a cycle.");
                }

                next.Add(descendant.Id);
            }

            frontier = next;
        }

        throw new ConstructionInspection.Application.Common.Exceptions.ConflictException(
            "construction_object.tree_invalid",
            "The Construction Object hierarchy exceeded the safety limit.");
    }

    public void Add(ConstructionObject constructionObject) =>
        context.ConstructionObjects.Add(constructionObject);
}
