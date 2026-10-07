using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Domain.Drawings;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class DrawingDocumentRepository(ApplicationDbContext context) : IDrawingDocumentRepository
{
    public void Add(DrawingDocument document) => context.DrawingDocuments.Add(document);
    public Task<bool> HasSha256Async(string sha256, CancellationToken cancellationToken) =>
        context.DrawingDocuments.AsNoTracking().AnyAsync(x => x.Sha256 == sha256, cancellationToken);
}
