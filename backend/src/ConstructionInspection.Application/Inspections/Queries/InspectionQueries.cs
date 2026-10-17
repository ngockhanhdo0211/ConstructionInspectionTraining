using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Domain.Inspections;
using MediatR;

namespace ConstructionInspection.Application.Inspections.Queries;

public sealed record GetInspectionsQuery(
    int PageNumber = 1, int PageSize = 20, string? Search = null,
    InspectionStatus? Status = null, Guid? ConstructionObjectId = null,
    Guid? ReviewerUserId = null, string SortBy = "createdAt", string SortDirection = "desc")
    : IRequest<PagedResult<InspectionListItemDto>>;

public sealed record GetInspectionDetailQuery(Guid Id) : IRequest<InspectionDetailDto>;

public sealed record GetFormTemplateOptionsQuery(
    int PageNumber = 1, int PageSize = 20, string? Search = null)
    : IRequest<PagedResult<FormTemplateOptionDto>>;

public sealed class GetInspectionsQueryHandler(ICurrentUser currentUser, IInspectionReadService readService)
    : IRequestHandler<GetInspectionsQuery, PagedResult<InspectionListItemDto>>
{
    public Task<PagedResult<InspectionListItemDto>> Handle(GetInspectionsQuery request, CancellationToken cancellationToken) =>
        readService.GetPageAsync(new(
            request.PageNumber, request.PageSize, request.Search, request.Status,
            request.ConstructionObjectId, request.ReviewerUserId, request.SortBy,
            request.SortDirection, InspectionAuthorization.RequireReader(currentUser)), cancellationToken);
}

public sealed class GetInspectionDetailQueryHandler(ICurrentUser currentUser, IInspectionReadService readService)
    : IRequestHandler<GetInspectionDetailQuery, InspectionDetailDto>
{
    public async Task<InspectionDetailDto> Handle(GetInspectionDetailQuery request, CancellationToken cancellationToken) =>
        await readService.GetDetailAsync(request.Id, InspectionAuthorization.RequireReader(currentUser), cancellationToken)
        ?? throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");
}

public sealed class GetFormTemplateOptionsQueryHandler(ICurrentUser currentUser, IInspectionReadService readService)
    : IRequestHandler<GetFormTemplateOptionsQuery, PagedResult<FormTemplateOptionDto>>
{
    public Task<PagedResult<FormTemplateOptionDto>> Handle(
        GetFormTemplateOptionsQuery request, CancellationToken cancellationToken)
    {
        InspectionAuthorization.RequireInspector(currentUser);
        return readService.GetFormTemplateOptionsAsync(
            new(request.PageNumber, request.PageSize, request.Search?.Trim()), cancellationToken);
    }
}
