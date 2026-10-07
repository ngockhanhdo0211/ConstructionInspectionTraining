using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Drawings.Interfaces;
using PdfSharp.Pdf.IO;

namespace ConstructionInspection.Infrastructure.Pdf;

public sealed class PdfSharpMetadataReader : IPdfMetadataReader
{
    public Task<PdfMetadata> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = PdfReader.Open(content, PdfDocumentOpenMode.Import);
            return Task.FromResult(new PdfMetadata(document.PageCount));
        }
        catch (PdfReaderException exception)
        {
            throw new UnsupportedMediaTypeException(
                exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                    ? "Encrypted or password-protected PDF documents are not supported."
                    : "The PDF document is invalid or unsupported.");
        }
    }
}
