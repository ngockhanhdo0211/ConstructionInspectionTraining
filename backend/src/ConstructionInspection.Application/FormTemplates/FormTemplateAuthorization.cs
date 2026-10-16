using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Application.FormTemplates;

internal static class FormTemplateAuthorization
{
    public static Guid RequireAdministrator(ICurrentUser currentUser)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException();
        if (!currentUser.IsInRole(ApplicationRoles.Administrator))
            throw new ForbiddenAccessException();
        return userId;
    }
}
