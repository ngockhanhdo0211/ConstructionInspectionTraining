using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using MediatR;

namespace ConstructionInspection.Application.Inspections.Queries;

public sealed record GetReviewerOptionsQuery(
    int PageNumber = 1, int PageSize = 20, string? Search = null)
    : IRequest<PagedResult<ReviewerOptionDto>>;

public sealed record GetInspectionHistoryQuery(
    Guid Id, int PageNumber = 1, int PageSize = 20)
    : IRequest<PagedResult<ProcessHistoryDto>>;

public sealed record GetInspectionRevisionsQuery(
    Guid Id, int PageNumber = 1, int PageSize = 20)
    : IRequest<PagedResult<InspectionRevisionListItemDto>>;

public sealed record GetInspectionRevisionDetailQuery(Guid Id, int RevisionNumber)
    : IRequest<InspectionRevisionDetailDto>;

public sealed class GetReviewerOptionsQueryHandler(
    ICurrentUser currentUser,
    IInspectionReviewerService reviewerService)
    : IRequestHandler<GetReviewerOptionsQuery, PagedResult<ReviewerOptionDto>>
{
    public Task<PagedResult<ReviewerOptionDto>> Handle(
        GetReviewerOptionsQuery request, CancellationToken cancellationToken)
    {
        InspectionAuthorization.RequireReviewerLookup(currentUser);
        return reviewerService.GetOptionsAsync(
            new(request.PageNumber, request.PageSize, request.Search?.Trim()), cancellationToken);
    }
}

public sealed class GetInspectionHistoryQueryHandler(
    ICurrentUser currentUser,
    IInspectionReadService readService)
    : IRequestHandler<GetInspectionHistoryQuery, PagedResult<ProcessHistoryDto>>
{
    public async Task<PagedResult<ProcessHistoryDto>> Handle(
        GetInspectionHistoryQuery request, CancellationToken cancellationToken) =>
        await readService.GetHistoryAsync(
            new(request.Id, request.PageNumber, request.PageSize,
                InspectionAuthorization.RequireReader(currentUser)), cancellationToken)
        ?? throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");
}

public sealed class GetInspectionRevisionsQueryHandler(
    ICurrentUser currentUser,
    IInspectionReadService readService)
    : IRequestHandler<GetInspectionRevisionsQuery, PagedResult<InspectionRevisionListItemDto>>
{
    public async Task<PagedResult<InspectionRevisionListItemDto>> Handle(
        GetInspectionRevisionsQuery request, CancellationToken cancellationToken) =>
        await readService.GetRevisionsAsync(
            new(request.Id, request.PageNumber, request.PageSize,
                InspectionAuthorization.RequireReader(currentUser)), cancellationToken)
        ?? throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");
}

public sealed class GetInspectionRevisionDetailQueryHandler(
    ICurrentUser currentUser,
    IInspectionReadService readService)
    : IRequestHandler<GetInspectionRevisionDetailQuery, InspectionRevisionDetailDto>
{
    public async Task<InspectionRevisionDetailDto> Handle(
        GetInspectionRevisionDetailQuery request, CancellationToken cancellationToken) =>
        await readService.GetRevisionDetailAsync(
            request.Id, request.RevisionNumber,
            InspectionAuthorization.RequireReader(currentUser), cancellationToken)
        ?? throw new ResourceNotFoundException("inspection.revision_not_found", "The Inspection revision was not found.");
}
