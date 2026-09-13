using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using MediatR;

namespace ConstructionInspection.Application.Authentication.Commands.Login;

public sealed class LoginCommandHandler(
    IIdentityService identityService,
    ITokenService tokenService)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var user = await identityService.AuthenticateAsync(
            request.Email,
            request.Password,
            cancellationToken);

        if (user is null)
        {
            throw new AuthenticationFailedException();
        }

        var token = tokenService.CreateToken(user);

        return new LoginResponse(
            token.AccessToken,
            "Bearer",
            token.ExpiresAtUtc,
            new LoginUserResponse(
                user.Id,
                user.Email,
                user.FullName,
                user.Roles));
    }
}
