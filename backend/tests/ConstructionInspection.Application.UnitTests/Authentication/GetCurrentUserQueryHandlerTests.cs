using ConstructionInspection.Application.Authentication.Queries.GetCurrentUser;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;

namespace ConstructionInspection.Application.UnitTests.Authentication;

public sealed class GetCurrentUserQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithAuthenticatedActiveUser_ReturnsProfile()
    {
        var user = new UserProfile(
            Guid.NewGuid(),
            "reviewer@example.test",
            "Test Reviewer",
            ["REVIEWER"]);
        var handler = new GetCurrentUserQueryHandler(
            new StubCurrentUser(user.Id),
            new StubIdentityService(user));

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.FullName, result.FullName);
        Assert.Equal(user.Roles, result.Roles);
    }

    [Fact]
    public async Task Handle_WithoutAuthenticatedUser_ThrowsAuthenticationFailure()
    {
        var handler = new GetCurrentUserQueryHandler(
            new StubCurrentUser(null),
            new StubIdentityService(null));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            handler.Handle(new GetCurrentUserQuery(), CancellationToken.None));
    }

    private sealed class StubCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public IReadOnlyCollection<string> Roles => [];
        public bool IsInRole(string role) => false;
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
}
