using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.DrawingMappings.Models;

public sealed record DrawingRectangleDto(decimal Left, decimal Bottom, decimal Right, decimal Top);

public sealed record MappingConstructionObjectDto(
    Guid Id, string Code, string Name, ConstructionObjectType ObjectType, bool IsActive);

public sealed record DrawingObjectMappingDto(
    Guid Id,
    Guid DrawingDocumentId,
    int PageNumber,
    MappingConstructionObjectDto ConstructionObject,
    DrawingMappingShapeType ShapeType,
    DrawingRectangleDto Geometry,
    DrawingObjectMappingStatus Status,
    DateTime CreatedAt,
    Guid CreatedByUserId,
    DateTime? UpdatedAt,
    Guid? UpdatedByUserId,
    DateTime? DeactivatedAt,
    Guid? DeactivatedByUserId,
    string RowVersion);

public sealed record DrawingPageMappingsDto(Guid DrawingId, int PageNumber, IReadOnlyCollection<DrawingObjectMappingDto> Items);

public sealed record DrawingMappingSourceDto(
    Guid MappingId,
    Guid DrawingId,
    string DrawingName,
    DrawingDocumentStatus DrawingStatus,
    int PageNumber,
    DrawingRectangleDto Geometry,
    DrawingObjectMappingStatus MappingStatus,
    bool ConstructionObjectIsActive,
    DateTime CreatedAt,
    string RowVersion);

public sealed record ConstructionObjectDrawingMappingsDto(
    Guid ConstructionObjectId,
    bool ConstructionObjectIsActive,
    PagedResult<DrawingMappingSourceDto> Mappings);

public sealed record DeactivatedDrawingObjectMappingDto(
    Guid Id,
    DrawingObjectMappingStatus Status,
    DateTime DeactivatedAt,
    Guid DeactivatedByUserId,
    string RowVersion);

public sealed record ConstructionObjectWithDrawingMappingDto(
    ConstructionObjectDetailDto ConstructionObject,
    DrawingObjectMappingDto Mapping);

public sealed record DrawingMappingQueryCriteria(int PageNumber, int PageSize);
