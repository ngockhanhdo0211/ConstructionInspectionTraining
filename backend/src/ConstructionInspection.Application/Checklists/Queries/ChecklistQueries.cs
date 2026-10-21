using ConstructionInspection.Application.Checklists.Interfaces;
using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Domain.Checklists;
using MediatR;

namespace ConstructionInspection.Application.Checklists.Queries;

public sealed record GetChecklistsQuery(int PageNumber = 1, int PageSize = 20, string? Search = null,
    ChecklistStatus? Status = null, Guid? ConstructionObjectId = null, Guid? ReviewerUserId = null,
    string SortBy = "createdAt", string SortDirection = "desc") : IRequest<PagedResult<ChecklistListItemDto>>;
public sealed record GetChecklistDetailQuery(Guid Id) : IRequest<ChecklistDetailDto>;
public sealed record GetChecklistHistoryQuery(Guid Id, int PageNumber = 1, int PageSize = 20)
    : IRequest<PagedResult<ChecklistHistoryDto>>;
public sealed record GetChecklistSubmissionsQuery(Guid Id, int PageNumber = 1, int PageSize = 20)
    : IRequest<PagedResult<ChecklistSubmissionListItemDto>>;
public sealed record GetChecklistSubmissionQuery(Guid Id, int SubmissionNumber) : IRequest<ChecklistSubmissionDetailDto>;

public sealed class GetChecklistsQueryHandler(ICurrentUser currentUser, IChecklistReadService readService)
    : IRequestHandler<GetChecklistsQuery, PagedResult<ChecklistListItemDto>>
{
    public Task<PagedResult<ChecklistListItemDto>> Handle(GetChecklistsQuery request, CancellationToken cancellationToken) =>
        readService.GetPageAsync(new(request.PageNumber, request.PageSize, request.Search?.Trim(), request.Status,
            request.ConstructionObjectId, request.ReviewerUserId, request.SortBy, request.SortDirection,
            ChecklistAuthorization.RequireReader(currentUser)), cancellationToken);
}

public sealed class GetChecklistDetailQueryHandler(ICurrentUser currentUser, IChecklistReadService readService)
    : IRequestHandler<GetChecklistDetailQuery, ChecklistDetailDto>
{
    public async Task<ChecklistDetailDto> Handle(GetChecklistDetailQuery request, CancellationToken cancellationToken) =>
        await readService.GetDetailAsync(request.Id, ChecklistAuthorization.RequireReader(currentUser), cancellationToken)
        ?? throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
}

public sealed class GetChecklistHistoryQueryHandler(ICurrentUser currentUser, IChecklistReadService readService)
    : IRequestHandler<GetChecklistHistoryQuery, PagedResult<ChecklistHistoryDto>>
{
    public async Task<PagedResult<ChecklistHistoryDto>> Handle(GetChecklistHistoryQuery request, CancellationToken cancellationToken) =>
        await readService.GetHistoryAsync(request.Id, request.PageNumber, request.PageSize,
            ChecklistAuthorization.RequireReader(currentUser), cancellationToken)
        ?? throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
}

public sealed class GetChecklistSubmissionsQueryHandler(ICurrentUser currentUser, IChecklistReadService readService)
    : IRequestHandler<GetChecklistSubmissionsQuery, PagedResult<ChecklistSubmissionListItemDto>>
{
    public async Task<PagedResult<ChecklistSubmissionListItemDto>> Handle(GetChecklistSubmissionsQuery request, CancellationToken cancellationToken) =>
        await readService.GetSubmissionsAsync(request.Id, request.PageNumber, request.PageSize,
            ChecklistAuthorization.RequireReader(currentUser), cancellationToken)
        ?? throw new ResourceNotFoundException("checklist.not_found", "The Checklist was not found.");
}

public sealed class GetChecklistSubmissionQueryHandler(ICurrentUser currentUser, IChecklistReadService readService)
    : IRequestHandler<GetChecklistSubmissionQuery, ChecklistSubmissionDetailDto>
{
    public async Task<ChecklistSubmissionDetailDto> Handle(GetChecklistSubmissionQuery request, CancellationToken cancellationToken) =>
        await readService.GetSubmissionAsync(request.Id, request.SubmissionNumber,
            ChecklistAuthorization.RequireReader(currentUser), cancellationToken)
        ?? throw new ResourceNotFoundException("checklist.submission_not_found", "The Checklist submission was not found.");
}
