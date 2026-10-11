using ConstructionInspection.Application.DrawingMappings.Models;

namespace ConstructionInspection.Application.DrawingMappings.Interfaces;

public interface IDrawingObjectMappingReadService
{
    Task<DrawingObjectMappingDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<DrawingPageMappingsDto?> GetPageAsync(Guid drawingId, int pageNumber, CancellationToken cancellationToken);
    Task<ConstructionObjectDrawingMappingsDto?> GetSourcesAsync(Guid constructionObjectId,
        DrawingMappingQueryCriteria criteria, CancellationToken cancellationToken);
}
