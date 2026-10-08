using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using ConstructionInspection.Domain.Drawings;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Queries.OpenDrawingContent;

public sealed class OpenDrawingContentQueryHandler(ICurrentUser user, IDrawingDocumentReadService reads)
    : IRequestHandler<OpenDrawingContentQuery, DrawingContentDescriptor>
{
    public async Task<DrawingContentDescriptor> Handle(OpenDrawingContentQuery request, CancellationToken cancellationToken)
    {
        DrawingAuthorization.RequireReader(user);
        var drawing = await reads.GetContentDescriptorAsync(request.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("drawing.not_found", "Drawing was not found.");
        if (drawing.Status != DrawingDocumentStatus.Ready)
            throw new ConflictException("drawing.not_ready", "Drawing content is not ready.");
        return drawing;
    }
}
