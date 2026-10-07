using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Application.Drawings;

internal static class DrawingAuthorization
{
    public static Guid RequireAdministrator(ICurrentUser user)
    {
        var id = user.UserId ?? throw new AuthenticationFailedException();
        if (!user.IsInRole(ApplicationRoles.Administrator)) throw new ForbiddenAccessException();
        return id;
    }

    public static void RequireReader(ICurrentUser user)
    {
        _ = user.UserId ?? throw new AuthenticationFailedException();
        if (!user.IsInRole(ApplicationRoles.Administrator) && !user.IsInRole(ApplicationRoles.Manager))
            throw new ForbiddenAccessException();
    }
}
