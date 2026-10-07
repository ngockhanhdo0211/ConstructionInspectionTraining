using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Queries.GetDrawings;

public sealed class GetDrawingsQueryHandler(ICurrentUser user, IDrawingDocumentReadService reads)
    : IRequestHandler<GetDrawingsQuery, PagedResult<DrawingListItemDto>>
{
    public Task<PagedResult<DrawingListItemDto>> Handle(GetDrawingsQuery r, CancellationToken ct)
    {
        DrawingAuthorization.RequireReader(user);
        return reads.GetPageAsync(new(r.PageNumber, r.PageSize, r.Search, r.SortBy, r.SortDirection), ct);
    }
}
