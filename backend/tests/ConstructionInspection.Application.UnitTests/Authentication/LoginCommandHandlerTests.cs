using ConstructionInspection.Application.Authentication.Commands.Login;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;

namespace ConstructionInspection.Application.UnitTests.Authentication;

public sealed class LoginCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCredentialsAreValid_ReturnsTokenAndUser()
    {
        var user = new UserProfile(
            Guid.NewGuid(),
            "inspector@example.test",
            "Test Inspector",
            ["INSPECTOR"]);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var handler = new LoginCommandHandler(
            new StubIdentityService(user),
            new StubTokenService(new IssuedToken("test-token", expiresAt)));

        var result = await handler.Handle(
            new LoginCommand(user.Email, "password"),
            CancellationToken.None);

        Assert.Equal("test-token", result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(user.Roles, result.User.Roles);
    }

    [Fact]
    public async Task Handle_WhenCredentialsAreInvalid_ThrowsGenericAuthenticationFailure()
    {
        var handler = new LoginCommandHandler(
            new StubIdentityService(null),
            new StubTokenService(new IssuedToken("unused", DateTimeOffset.UtcNow)));

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            handler.Handle(
                new LoginCommand("missing@example.test", "wrong"),
                CancellationToken.None));

        Assert.Equal("Invalid email or password.", exception.Message);
    }

    [Fact]
    public async Task Handle_WhenIdentityRejectsInactiveUser_ThrowsSameAuthenticationFailure()
    {
        var handler = new LoginCommandHandler(
            new StubIdentityService(null),
            new StubTokenService(new IssuedToken("unused", DateTimeOffset.UtcNow)));

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            handler.Handle(
                new LoginCommand("inactive@example.test", "password"),
                CancellationToken.None));

        Assert.Equal("Invalid email or password.", exception.Message);
    }

    private sealed class StubIdentityService(UserProfile? user) : IIdentityService
    {
        public Task<UserProfile?> AuthenticateAsync(
            string email,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(user);

        public Task<UserProfile?> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(user);
    }

    private sealed class StubTokenService(IssuedToken token) : ITokenService
    {
        public IssuedToken CreateToken(UserProfile user) => token;
    }
}
