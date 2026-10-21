using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Application.Checklists;

internal static class ChecklistAuthorization
{
    public static Guid RequireInspector(ICurrentUser user) => RequireRole(user, ApplicationRoles.Inspector);
    public static Guid RequireReviewer(ICurrentUser user) => RequireRole(user, ApplicationRoles.Reviewer);
    public static ChecklistReadScope RequireReader(ICurrentUser user)
    {
        var id = RequireAuthenticated(user);
        var inspector = user.IsInRole(ApplicationRoles.Inspector);
        var reviewer = user.IsInRole(ApplicationRoles.Reviewer);
        var full = user.IsInRole(ApplicationRoles.Manager) || user.IsInRole(ApplicationRoles.Administrator);
        if (!inspector && !reviewer && !full) throw new ForbiddenAccessException();
        return new(id, full, inspector, reviewer);
    }

    private static Guid RequireRole(ICurrentUser user, string role)
    {
        var id = RequireAuthenticated(user);
        if (!user.IsInRole(role)) throw new ForbiddenAccessException();
        return id;
    }

    private static Guid RequireAuthenticated(ICurrentUser user) =>
        user.UserId ?? throw new AuthenticationFailedException();
}
