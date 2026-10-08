using ConstructionInspection.Application.Drawings.Models;

namespace ConstructionInspection.Api.Drawings;

public interface IDrawingContentResponseWriter
{
    Task WriteAsync(HttpContext context, DrawingContentDescriptor drawing, CancellationToken cancellationToken);
}
