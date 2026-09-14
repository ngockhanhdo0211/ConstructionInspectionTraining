using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Application.ConstructionObjects;

internal static class ConstructionObjectAuthorization
{
    public static Guid RequireAdministrator(ICurrentUser currentUser)
    {
        var userId = RequireAuthenticated(currentUser);
        if (!currentUser.IsInRole(ApplicationRoles.Administrator))
        {
            throw new ForbiddenAccessException();
        }

        return userId;
    }

    public static bool CanReadInactive(ICurrentUser currentUser)
    {
        RequireReader(currentUser);
        return currentUser.IsInRole(ApplicationRoles.Manager)
            || currentUser.IsInRole(ApplicationRoles.Administrator);
    }

    private static void RequireReader(ICurrentUser currentUser)
    {
        RequireAuthenticated(currentUser);
        if (!currentUser.Roles.Any(role => ApplicationRoles.All.Contains(role)))
        {
            throw new ForbiddenAccessException();
        }
    }

    private static Guid RequireAuthenticated(ICurrentUser currentUser) =>
        currentUser.UserId ?? throw new AuthenticationFailedException();
}
