using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Dashboard.Interfaces;
using ConstructionInspection.Application.Dashboard.Models;
using MediatR;

namespace ConstructionInspection.Application.Dashboard.Queries;

public sealed record GetDashboardQuery : IRequest<DashboardResponseDto>;

public sealed class GetDashboardQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IDashboardReadService readService)
    : IRequestHandler<GetDashboardQuery, DashboardResponseDto>
{
    public Task<DashboardResponseDto> Handle(
        GetDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var scope = CreateScope(currentUser);
        var nowUtc = clock.UtcNow;
        return readService.GetAsync(scope, nowUtc, cancellationToken);
    }

    private static DashboardReadScope CreateScope(ICurrentUser user)
    {
        var userId = user.UserId ?? throw new AuthenticationFailedException();
        var isAdministrator = user.IsInRole(ApplicationRoles.Administrator);
        var isManager = user.IsInRole(ApplicationRoles.Manager);
        var isInspector = user.IsInRole(ApplicationRoles.Inspector);
        var isReviewer = user.IsInRole(ApplicationRoles.Reviewer);

        if (!isAdministrator && !isManager && !isInspector && !isReviewer)
            throw new ForbiddenAccessException();

        var roles = ApplicationRoles.All
            .Where(user.IsInRole)
            .ToArray();
        var scopeMode = isAdministrator || isManager
            ? "Full"
            : isInspector && isReviewer
                ? "OwnAndAssigned"
                : isInspector ? "Own" : "Assigned";

        return new DashboardReadScope(
            userId,
            isAdministrator || isManager,
            isInspector,
            isReviewer,
            isManager,
            isAdministrator,
            roles,
            scopeMode);
    }
}
