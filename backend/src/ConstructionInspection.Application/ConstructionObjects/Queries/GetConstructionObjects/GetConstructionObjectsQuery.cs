using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjects;

public sealed record GetConstructionObjectsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    ConstructionObjectType? ObjectType = null,
    bool? IsActive = null,
    ParentFilterMode ParentFilterMode = ParentFilterMode.All,
    Guid? ParentId = null,
    string SortBy = "name",
    string SortDirection = "asc")
    : IRequest<PagedResult<ConstructionObjectListItemDto>>;
