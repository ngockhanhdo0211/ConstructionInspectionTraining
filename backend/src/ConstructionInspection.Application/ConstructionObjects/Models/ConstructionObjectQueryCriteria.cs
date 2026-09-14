using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Application.ConstructionObjects.Models;

public enum ParentFilterMode
{
    All,
    Roots,
    Specific
}

public sealed record ConstructionObjectQueryCriteria(
    int PageNumber,
    int PageSize,
    string? Search,
    ConstructionObjectType? ObjectType,
    bool? IsActive,
    ParentFilterMode ParentFilterMode,
    Guid? ParentId,
    string SortBy,
    string SortDirection,
    bool IncludeInactive);
