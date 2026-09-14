using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Models;

namespace ConstructionInspection.Application.ConstructionObjects.Interfaces;

public interface IConstructionObjectReadService
{
    Task<PagedResult<ConstructionObjectListItemDto>> GetPageAsync(
        ConstructionObjectQueryCriteria criteria,
        CancellationToken cancellationToken);

    Task<ConstructionObjectDetailDto?> GetDetailAsync(
        Guid id,
        bool includeInactive,
        CancellationToken cancellationToken);
}
