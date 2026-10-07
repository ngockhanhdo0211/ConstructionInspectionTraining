using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.Drawings.Interfaces;

public interface IDrawingDocumentRepository
{
    void Add(DrawingDocument document);
    Task<bool> HasSha256Async(string sha256, CancellationToken cancellationToken);
}
