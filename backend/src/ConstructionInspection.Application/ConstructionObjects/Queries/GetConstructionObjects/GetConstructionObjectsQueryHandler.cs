using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjects;

public sealed class GetConstructionObjectsQueryHandler(
    ICurrentUser currentUser,
    IConstructionObjectReadService readService)
    : IRequestHandler<GetConstructionObjectsQuery, PagedResult<ConstructionObjectListItemDto>>
{
    public Task<PagedResult<ConstructionObjectListItemDto>> Handle(
        GetConstructionObjectsQuery request,
        CancellationToken cancellationToken)
    {
        var includeInactive = ConstructionObjectAuthorization.CanReadInactive(currentUser);
        if (!includeInactive && request.IsActive is false)
        {
            throw new ForbiddenAccessException(
                "Inspector and Reviewer can only view active Construction Objects.");
        }

        var criteria = new ConstructionObjectQueryCriteria(
            request.PageNumber,
            request.PageSize,
            request.Search?.Trim(),
            request.ObjectType,
            includeInactive ? request.IsActive : true,
            request.ParentFilterMode,
            request.ParentId,
            request.SortBy,
            request.SortDirection,
            includeInactive);

        return readService.GetPageAsync(criteria, cancellationToken);
    }
}
