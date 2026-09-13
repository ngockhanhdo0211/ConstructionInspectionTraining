using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using MediatR;

namespace ConstructionInspection.Application.Authentication.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identityService)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<CurrentUserResponse> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            throw new AuthenticationFailedException();
        }

        var user = await identityService.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new AuthenticationFailedException();
        }

        return new CurrentUserResponse(user.Id, user.Email, user.FullName, user.Roles);
    }
}
