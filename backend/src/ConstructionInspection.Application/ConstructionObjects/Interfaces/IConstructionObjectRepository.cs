using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Application.ConstructionObjects.Interfaces;

public interface IConstructionObjectRepository
{
    Task<ConstructionObject?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<ConstructionObjectHierarchyNode?> GetHierarchyNodeAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> HasActiveDescendantsAsync(Guid id, CancellationToken cancellationToken);
    void Add(ConstructionObject constructionObject);
}
