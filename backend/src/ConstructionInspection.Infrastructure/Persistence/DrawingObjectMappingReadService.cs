using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using ConstructionInspection.Domain.DrawingMappings;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class DrawingObjectMappingReadService(ApplicationDbContext context)
    : IDrawingObjectMappingReadService
{
    public async Task<DrawingObjectMappingDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await (from mapping in context.DrawingObjectMappings.AsNoTracking()
                          join constructionObject in context.ConstructionObjects.AsNoTracking()
                              on mapping.ConstructionObjectId equals constructionObject.Id
                          where mapping.Id == id
                          select new MappingProjection(mapping.Id, mapping.DrawingDocumentId, mapping.PageNumber,
                              constructionObject.Id, constructionObject.Code, constructionObject.Name,
                              constructionObject.ObjectType, constructionObject.IsActive, mapping.ShapeType,
                              mapping.Left, mapping.Bottom, mapping.Right, mapping.Top, mapping.Status,
                              mapping.CreatedAt, mapping.CreatedByUserId, mapping.UpdatedAt, mapping.UpdatedByUserId,
                              mapping.DeactivatedAt, mapping.DeactivatedByUserId, mapping.RowVersion))
            .SingleOrDefaultAsync(ct);
        return item is null ? null : ToDto(item);
    }

    public async Task<DrawingPageMappingsDto?> GetPageAsync(Guid drawingId, int pageNumber, CancellationToken ct)
    {
        if (!await context.DrawingDocuments.AsNoTracking().AnyAsync(x => x.Id == drawingId, ct)) return null;
        var items = await (from mapping in context.DrawingObjectMappings.AsNoTracking()
                           join constructionObject in context.ConstructionObjects.AsNoTracking()
                               on mapping.ConstructionObjectId equals constructionObject.Id
                           where mapping.DrawingDocumentId == drawingId
                               && mapping.PageNumber == pageNumber
                               && mapping.Status == DrawingObjectMappingStatus.Active
                           orderby mapping.CreatedAt, mapping.Id
                           select new MappingProjection(mapping.Id, mapping.DrawingDocumentId, mapping.PageNumber,
                               constructionObject.Id, constructionObject.Code, constructionObject.Name,
                               constructionObject.ObjectType, constructionObject.IsActive, mapping.ShapeType,
                               mapping.Left, mapping.Bottom, mapping.Right, mapping.Top, mapping.Status,
                               mapping.CreatedAt, mapping.CreatedByUserId, mapping.UpdatedAt, mapping.UpdatedByUserId,
                               mapping.DeactivatedAt, mapping.DeactivatedByUserId, mapping.RowVersion))
            .Take(500)
            .ToArrayAsync(ct);
        return new DrawingPageMappingsDto(drawingId, pageNumber, items.Select(ToDto).ToArray());
    }

    public async Task<ConstructionObjectDrawingMappingsDto?> GetSourcesAsync(
        Guid constructionObjectId, DrawingMappingQueryCriteria criteria, CancellationToken ct)
    {
        var constructionObject = await context.ConstructionObjects.AsNoTracking()
            .Where(x => x.Id == constructionObjectId)
            .Select(x => new { x.Id, x.IsActive })
            .SingleOrDefaultAsync(ct);
        if (constructionObject is null) return null;

        var total = await context.DrawingObjectMappings.AsNoTracking()
            .CountAsync(x => x.ConstructionObjectId == constructionObjectId, ct);
        var rows = await (from mapping in context.DrawingObjectMappings.AsNoTracking()
                          join drawing in context.DrawingDocuments.AsNoTracking()
                              on mapping.DrawingDocumentId equals drawing.Id
                          where mapping.ConstructionObjectId == constructionObjectId
                          orderby mapping.CreatedAt descending, mapping.Id
                          select new SourceProjection(mapping.Id, drawing.Id, drawing.OriginalFileName,
                              drawing.Status, mapping.PageNumber, mapping.Left, mapping.Bottom, mapping.Right,
                              mapping.Top, mapping.Status, mapping.CreatedAt, mapping.RowVersion))
            .Skip((criteria.PageNumber - 1) * criteria.PageSize).Take(criteria.PageSize).ToArrayAsync(ct);
        var items = rows.Select(x => new DrawingMappingSourceDto(x.MappingId, x.DrawingId,
            x.DrawingName, x.DrawingStatus, x.PageNumber,
            new DrawingRectangleDto(x.Left, x.Bottom, x.Right, x.Top), x.MappingStatus,
            constructionObject.IsActive, x.CreatedAt, Convert.ToBase64String(x.RowVersion))).ToArray();
        var page = new PagedResult<DrawingMappingSourceDto>(items, criteria.PageNumber, criteria.PageSize,
            total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)criteria.PageSize));
        return new ConstructionObjectDrawingMappingsDto(constructionObject.Id, constructionObject.IsActive, page);
    }

    private static DrawingObjectMappingDto ToDto(MappingProjection x) => new(
        x.MappingId, x.DrawingId, x.PageNumber,
        new MappingConstructionObjectDto(x.ObjectId, x.ObjectCode, x.ObjectName, x.ObjectType, x.ObjectIsActive),
        x.ShapeType, new DrawingRectangleDto(x.Left, x.Bottom, x.Right, x.Top), x.Status,
        x.CreatedAt, x.CreatedByUserId, x.UpdatedAt, x.UpdatedByUserId,
        x.DeactivatedAt, x.DeactivatedByUserId, Convert.ToBase64String(x.RowVersion));

    private sealed record MappingProjection(Guid MappingId, Guid DrawingId, int PageNumber,
        Guid ObjectId, string ObjectCode, string ObjectName,
        ConstructionInspection.Domain.ConstructionObjects.ConstructionObjectType ObjectType, bool ObjectIsActive,
        DrawingMappingShapeType ShapeType, decimal Left, decimal Bottom, decimal Right, decimal Top,
        DrawingObjectMappingStatus Status, DateTime CreatedAt, Guid CreatedByUserId,
        DateTime? UpdatedAt, Guid? UpdatedByUserId, DateTime? DeactivatedAt,
        Guid? DeactivatedByUserId, byte[] RowVersion);

    private sealed record SourceProjection(Guid MappingId, Guid DrawingId, string DrawingName,
        ConstructionInspection.Domain.Drawings.DrawingDocumentStatus DrawingStatus, int PageNumber,
        decimal Left, decimal Bottom, decimal Right, decimal Top,
        DrawingObjectMappingStatus MappingStatus, DateTime CreatedAt, byte[] RowVersion);
}
