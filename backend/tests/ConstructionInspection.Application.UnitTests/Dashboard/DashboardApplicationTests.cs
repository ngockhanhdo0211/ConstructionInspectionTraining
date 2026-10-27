using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Dashboard.Interfaces;
using ConstructionInspection.Application.Dashboard.Models;
using ConstructionInspection.Application.Dashboard.Queries;

namespace ConstructionInspection.Application.UnitTests.Dashboard;

public sealed class DashboardApplicationTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 10, 26, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Anonymous_IsRejectedBeforeReadService()
    {
        var service = new FakeDashboardReadService();
        var handler = CreateHandler(new FakeCurrentUser(null, []), service: service);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            handler.Handle(new GetDashboardQuery(), CancellationToken.None));

        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task AuthenticatedUnsupportedRole_IsForbiddenBeforeReadService()
    {
        var service = new FakeDashboardReadService();
        var handler = CreateHandler(new FakeCurrentUser(UserId, ["OTHER"]), service: service);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new GetDashboardQuery(), CancellationToken.None));

        Assert.Equal(0, service.CallCount);
    }

    [Theory]
    [InlineData(ApplicationRoles.Inspector, false, true, false, false, false, "Own")]
    [InlineData(ApplicationRoles.Reviewer, false, false, true, false, false, "Assigned")]
    [InlineData(ApplicationRoles.Manager, true, false, false, true, false, "Full")]
    [InlineData(ApplicationRoles.Administrator, true, false, false, false, true, "Full")]
    public async Task SupportedRole_MapsExactReadScope(
        string role,
        bool canReadAll,
        bool isInspector,
        bool isReviewer,
        bool isManager,
        bool isAdministrator,
        string scopeMode)
    {
        var service = new FakeDashboardReadService();
        var handler = CreateHandler(new FakeCurrentUser(UserId, [role]), service: service);

        await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        var scope = Assert.IsType<DashboardReadScope>(service.Scope);
        Assert.Equal(UserId, scope.UserId);
        Assert.Equal(canReadAll, scope.CanReadAll);
        Assert.Equal(isInspector, scope.IsInspector);
        Assert.Equal(isReviewer, scope.IsReviewer);
        Assert.Equal(isManager, scope.IsManager);
        Assert.Equal(isAdministrator, scope.IsAdministrator);
        Assert.Equal(scopeMode, scope.ScopeMode);
        Assert.Equal([role], scope.Roles);
    }

    [Fact]
    public async Task MultiRole_UsesUnionScope()
    {
        var service = new FakeDashboardReadService();
        var handler = CreateHandler(new FakeCurrentUser(
            UserId, [ApplicationRoles.Inspector, ApplicationRoles.Reviewer]), service: service);

        await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.False(service.Scope!.CanReadAll);
        Assert.True(service.Scope.IsInspector);
        Assert.True(service.Scope.IsReviewer);
        Assert.Equal("OwnAndAssigned", service.Scope.ScopeMode);
    }

    [Fact]
    public async Task ClockIsReadOnce_AndCancellationTokenIsForwarded()
    {
        var clock = new CountingClock();
        var service = new FakeDashboardReadService();
        using var source = new CancellationTokenSource();
        var handler = CreateHandler(
            new FakeCurrentUser(UserId, [ApplicationRoles.Inspector]), clock, service);

        await handler.Handle(new GetDashboardQuery(), source.Token);

        Assert.Equal(1, clock.ReadCount);
        Assert.Equal(Now, service.NowUtc);
        Assert.Equal(source.Token, service.CancellationToken);
    }

    [Fact]
    public void ResponseContract_DoesNotExposeSensitiveBusinessContentOrUrls()
    {
        var propertyNames = typeof(DashboardResponseDto).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(DashboardResponseDto).Namespace)
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var forbidden in new[]
        {
            "DataJson", "SchemaJson", "Question", "Response", "Clarification",
            "Note", "StorageKey", "Jwt", "Url"
        })
        {
            Assert.DoesNotContain(forbidden, propertyNames);
        }
    }

    [Fact]
    public async Task AdministratorScope_IsMonitoringOnly()
    {
        var service = new FakeDashboardReadService();
        var handler = CreateHandler(
            new FakeCurrentUser(UserId, [ApplicationRoles.Administrator]), service: service);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.True(service.Scope!.IsAdministrator);
        Assert.Equal(0, result.InspectionSummary.AwaitingMyAction);
        Assert.Equal(0, result.ChecklistSummary.AwaitingMyAction);
        Assert.Equal(0, result.RfiSummary.AwaitingMyAction);
        Assert.Empty(result.ActionQueue);
    }

    [Fact]
    public async Task ManagerScope_DoesNotTreatAllOpenWorkAsAwaitingAction()
    {
        var service = new FakeDashboardReadService
        {
            Response = EmptyResponse() with
            {
                RfiSummary = new RfiDashboardSummaryDto(
                    8, 6, 1, 2, 1, 1, EmptyRfiStatusCounts())
            }
        };
        var handler = CreateHandler(
            new FakeCurrentUser(UserId, [ApplicationRoles.Manager]), service: service);

        var result = await handler.Handle(new GetDashboardQuery(), CancellationToken.None);

        Assert.Equal(6, result.RfiSummary.OpenWork);
        Assert.Equal(1, result.RfiSummary.AwaitingMyAction);
    }

    private static GetDashboardQueryHandler CreateHandler(
        ICurrentUser user,
        IClock? clock = null,
        IDashboardReadService? service = null) =>
        new(user, clock ?? new CountingClock(), service ?? new FakeDashboardReadService());

    private static DashboardResponseDto EmptyResponse() => new(
        Now,
        [],
        "Own",
        new(0, 0, 0, EmptyInspectionStatusCounts()),
        new(0, 0, 0, EmptyChecklistStatusCounts()),
        new(0, 0, 0, 0, 0, 0, EmptyRfiStatusCounts()),
        []);

    private static InspectionDashboardStatusCountsDto EmptyInspectionStatusCounts() =>
        new(0, 0, 0, 0, 0, 0);

    private static ChecklistDashboardStatusCountsDto EmptyChecklistStatusCounts() =>
        new(0, 0, 0, 0, 0, 0, 0);

    private static RfiDashboardStatusCountsDto EmptyRfiStatusCounts() =>
        new(0, 0, 0, 0, 0);

    private sealed class FakeCurrentUser(Guid? userId, IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public IReadOnlyCollection<string> Roles { get; } = roles;
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);
    }

    private sealed class CountingClock : IClock
    {
        public int ReadCount { get; private set; }
        public DateTime UtcNow
        {
            get
            {
                ReadCount++;
                return Now;
            }
        }
    }

    private sealed class FakeDashboardReadService : IDashboardReadService
    {
        public int CallCount { get; private set; }
        public DashboardReadScope? Scope { get; private set; }
        public DateTime NowUtc { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public DashboardResponseDto Response { get; set; } = EmptyResponse();

        public Task<DashboardResponseDto> GetAsync(
            DashboardReadScope scope,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Scope = scope;
            NowUtc = nowUtc;
            CancellationToken = cancellationToken;
            return Task.FromResult(Response with
            {
                GeneratedAtUtc = nowUtc,
                Roles = scope.Roles,
                ScopeMode = scope.ScopeMode
            });
        }
    }
}
