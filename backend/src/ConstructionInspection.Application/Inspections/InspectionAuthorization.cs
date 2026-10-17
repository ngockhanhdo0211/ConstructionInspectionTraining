using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Application.Inspections;

internal static class InspectionAuthorization
{
    public static Guid RequireInspector(ICurrentUser user)
    {
        var id = RequireAuthenticated(user);
        if (!user.IsInRole(ApplicationRoles.Inspector)) throw new ForbiddenAccessException();
        return id;
    }

    public static InspectionReadScope RequireReader(ICurrentUser user)
    {
        var id = RequireAuthenticated(user);
        var administrator = user.IsInRole(ApplicationRoles.Administrator);
        var manager = user.IsInRole(ApplicationRoles.Manager);
        var inspector = user.IsInRole(ApplicationRoles.Inspector);
        var reviewer = user.IsInRole(ApplicationRoles.Reviewer);
        if (!administrator && !manager && !inspector && !reviewer)
            throw new ForbiddenAccessException();
        return new(id, administrator || manager, inspector, reviewer);
    }

    private static Guid RequireAuthenticated(ICurrentUser user) =>
        user.UserId ?? throw new AuthenticationFailedException();
}

public sealed record InspectionReadScope(Guid UserId, bool CanReadAll, bool IsInspector, bool IsReviewer);
