using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Rfis.Interfaces;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Domain.Rfis;
using MediatR;

namespace ConstructionInspection.Application.Rfis.Queries;

public sealed record GetRfisQuery(int PageNumber = 1, int PageSize = 20, string? Search = null,
    RfiStatus? Status = null, Guid? ConstructionObjectId = null, bool AssignedToMe = false,
    bool? Overdue = null, string SortBy = "createdAt", string SortDirection = "desc")
    : IRequest<PagedResult<RfiListItemDto>>;
public sealed record GetRfiDetailQuery(Guid Id) : IRequest<RfiDetailDto>;
public sealed record GetRfiHistoryQuery(Guid Id, int PageNumber = 1, int PageSize = 20)
    : IRequest<PagedResult<RfiHistoryDto>>;
public sealed record GetRfiResponderOptionsQuery(int PageNumber = 1, int PageSize = 20, string? Search = null)
    : IRequest<PagedResult<RfiResponderOptionDto>>;

public sealed class GetRfisQueryHandler(ICurrentUser user, IClock clock, IRfiReadService service)
    : IRequestHandler<GetRfisQuery, PagedResult<RfiListItemDto>>
{
    public Task<PagedResult<RfiListItemDto>> Handle(GetRfisQuery r, CancellationToken ct) =>
        service.GetPageAsync(new(r.PageNumber, r.PageSize, r.Search?.Trim(), r.Status,
            r.ConstructionObjectId, r.AssignedToMe, r.Overdue, r.SortBy, r.SortDirection,
            RfiAuthorization.RequireReader(user), clock.UtcNow), ct);
}
public sealed class GetRfiDetailQueryHandler(ICurrentUser user, IRfiReadService service)
    : IRequestHandler<GetRfiDetailQuery, RfiDetailDto>
{
    public async Task<RfiDetailDto> Handle(GetRfiDetailQuery r, CancellationToken ct) =>
        await service.GetDetailAsync(r.Id, RfiAuthorization.RequireReader(user), ct)
        ?? throw new ResourceNotFoundException("rfi.not_found", "The RFI was not found.");
}
public sealed class GetRfiHistoryQueryHandler(ICurrentUser user, IRfiReadService service)
    : IRequestHandler<GetRfiHistoryQuery, PagedResult<RfiHistoryDto>>
{
    public async Task<PagedResult<RfiHistoryDto>> Handle(GetRfiHistoryQuery r, CancellationToken ct) =>
        await service.GetHistoryAsync(r.Id, r.PageNumber, r.PageSize, RfiAuthorization.RequireReader(user), ct)
        ?? throw new ResourceNotFoundException("rfi.not_found", "The RFI was not found.");
}
public sealed class GetRfiResponderOptionsQueryHandler(ICurrentUser user, IRfiReviewerService service)
    : IRequestHandler<GetRfiResponderOptionsQuery, PagedResult<RfiResponderOptionDto>>
{
    public Task<PagedResult<RfiResponderOptionDto>> Handle(GetRfiResponderOptionsQuery r, CancellationToken ct)
    {
        RfiAuthorization.RequireResponderLookup(user);
        return service.GetOptionsAsync(r.PageNumber, r.PageSize, r.Search?.Trim(), ct);
    }
}
