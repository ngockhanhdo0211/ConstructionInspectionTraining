namespace ConstructionInspection.Application.Drawings.Interfaces;

public interface IPdfMetadataReader
{
    Task<PdfMetadata> ReadAsync(Stream content, CancellationToken cancellationToken);
}

public sealed record PdfMetadata(int PageCount);
