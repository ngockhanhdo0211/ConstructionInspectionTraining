using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Models;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Queries.GetDrawings;

public sealed record GetDrawingsQuery(int PageNumber, int PageSize, string? Search, string SortBy, string SortDirection)
    : IRequest<PagedResult<DrawingListItemDto>>;
