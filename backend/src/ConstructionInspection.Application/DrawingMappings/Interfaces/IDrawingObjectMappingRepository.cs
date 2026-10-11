using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.DrawingMappings.Interfaces;

public interface IDrawingObjectMappingRepository
{
    Task<DrawingObjectMapping?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<DrawingDocumentState?> GetDrawingStateAsync(Guid id, CancellationToken cancellationToken);
    Task<ConstructionObjectState?> GetConstructionObjectStateAsync(Guid id, CancellationToken cancellationToken);
    Task AcquirePageWriteLockAsync(Guid drawingId, int pageNumber, CancellationToken cancellationToken);
    Task<int> CountActiveOnPageAsync(Guid drawingId, int pageNumber, CancellationToken cancellationToken);
    Task<bool> ExactActiveExistsAsync(Guid drawingId, int pageNumber, Guid constructionObjectId,
        DrawingRectangle rectangle, Guid? excludingId, CancellationToken cancellationToken);
    void Add(DrawingObjectMapping mapping);
}

public sealed record DrawingDocumentState(Guid Id, int PageCount, DrawingDocumentStatus Status);
public sealed record ConstructionObjectState(Guid Id, bool IsActive);
