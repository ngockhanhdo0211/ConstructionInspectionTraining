using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Models;

namespace ConstructionInspection.Application.Drawings.Interfaces;

public interface IDrawingDocumentReadService
{
    Task<PagedResult<DrawingListItemDto>> GetPageAsync(DrawingQueryCriteria criteria, CancellationToken cancellationToken);
    Task<DrawingDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<DrawingContentDescriptor?> GetContentDescriptorAsync(Guid id, CancellationToken cancellationToken);
}
