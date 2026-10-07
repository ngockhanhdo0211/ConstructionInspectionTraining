using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Queries.GetDrawingDetail;

public sealed class GetDrawingDetailQueryHandler(ICurrentUser user, IDrawingDocumentReadService reads)
    : IRequestHandler<GetDrawingDetailQuery, DrawingDetailDto>
{
    public async Task<DrawingDetailDto> Handle(GetDrawingDetailQuery request, CancellationToken cancellationToken)
    {
        DrawingAuthorization.RequireReader(user);
        return await reads.GetDetailAsync(request.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("drawing.not_found", "Drawing was not found.");
    }
}
