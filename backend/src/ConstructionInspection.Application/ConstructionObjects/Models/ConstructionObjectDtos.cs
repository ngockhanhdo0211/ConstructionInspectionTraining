using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Application.ConstructionObjects.Models;

public sealed record ConstructionObjectListItemDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    ConstructionObjectType ObjectType,
    string? Location,
    bool IsActive,
    string RowVersion);

public sealed record ConstructionObjectBreadcrumbItemDto(
    Guid Id,
    string Code,
    string Name,
    ConstructionObjectType ObjectType);

public sealed record ConstructionObjectDetailDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    ConstructionObjectType ObjectType,
    string? Location,
    string? Description,
    bool IsActive,
    ConstructionObjectBreadcrumbItemDto? Parent,
    IReadOnlyCollection<ConstructionObjectBreadcrumbItemDto> Breadcrumb,
    bool HasChildren,
    int DirectChildrenCount,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    Guid? UpdatedByUserId,
    DateTime? UpdatedAt,
    string RowVersion);

public sealed record DeactivatedConstructionObjectDto(
    Guid Id,
    bool IsActive,
    Guid UpdatedByUserId,
    DateTime UpdatedAt,
    string RowVersion);

public sealed record ConstructionObjectHierarchyNode(
    Guid Id,
    Guid? ParentId,
    ConstructionObjectType ObjectType,
    bool IsActive);
