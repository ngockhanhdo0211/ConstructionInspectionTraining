using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Rfis.Commands;
using ConstructionInspection.Application.Rfis.Interfaces;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Application.Rfis.Queries;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Rfis;

namespace ConstructionInspection.Application.UnitTests.Rfis;

public sealed class RfiApplicationTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Reviewer = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid ObjectId = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_UsesSequenceAndAtomicFoundation()
    {
        var repo = new FakeRepo { Object = new(ObjectId, true) };
        var unit = new FakeUnit();
        var result = await new CreateRfiCommandHandler(User(ApplicationRoles.Inspector), new Clock(), repo,
            new Sequence(12), unit).Handle(new(ObjectId, "Subject", "Question"), CancellationToken.None);
        Assert.Equal("RFI-2026-000012", repo.Rfi!.Number);
        Assert.Equal(ProcessEntityType.Rfi, repo.Process!.EntityType);
        Assert.Equal("Create", repo.Histories.Single().Action);
        Assert.Equal(RfiStatus.Draft, result.Status);
        Assert.True(unit.Committed);
    }

    [Theory]
    [InlineData(ApplicationRoles.Reviewer)]
    [InlineData(ApplicationRoles.Manager)]
    [InlineData(ApplicationRoles.Administrator)]
    public async Task Create_NonInspectorForbidden(string role) =>
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new CreateRfiCommandHandler(User(role),
            new Clock(), new FakeRepo(), new Sequence(1), new FakeUnit())
            .Handle(new(ObjectId, "S", "Q"), CancellationToken.None));

    [Fact]
    public async Task Submit_AssignsEligibleReviewerAndSetsSla()
    {
        var repo = Ready();
        var result = await new SubmitRfiCommandHandler(User(ApplicationRoles.Inspector), new Clock(), repo,
            new Reviewers(), new FakeUnit()).Handle(new(repo.Rfi!.Id, Reviewer, "", ""), CancellationToken.None);
        Assert.Equal(RfiStatus.Open, result.Status);
        Assert.Equal(Reviewer, result.AssignedToUserId);
        Assert.Equal(Now.AddHours(72), result.DueAtUtc);
        Assert.Equal("Submit", repo.Histories.Single().Action);
    }

    [Fact]
    public async Task Submit_RejectsCreatorAsReviewer()
    {
        var repo = Ready();
        var error = await Assert.ThrowsAsync<ConflictException>(() => new SubmitRfiCommandHandler(
            User(ApplicationRoles.Inspector), new Clock(), repo, new Reviewers(), new FakeUnit())
            .Handle(new(repo.Rfi!.Id, Actor, "", ""), CancellationToken.None));
        Assert.Equal("rfi.reviewer_invalid", error.Code);
    }

    [Fact]
    public async Task Submit_RejectsReviewerWhoseRoleWasRemoved()
    {
        var repo = Ready();
        var reviewers = new Reviewers { HasRole = false };
        var error = await Assert.ThrowsAsync<ConflictException>(() => new SubmitRfiCommandHandler(
            User(ApplicationRoles.Inspector), new Clock(), repo, reviewers, new FakeUnit())
            .Handle(new(repo.Rfi!.Id, Reviewer, "", ""), CancellationToken.None));
        Assert.Equal("rfi.reviewer_invalid", error.Code);
    }

    [Fact]
    public async Task Submit_StaleRowVersionDoesNotWriteHistory()
    {
        var repo = Ready();
        await Assert.ThrowsAsync<ConflictException>(() => new SubmitRfiCommandHandler(
            User(ApplicationRoles.Inspector), new Clock(), repo, new Reviewers(), new FakeUnit())
            .Handle(new(repo.Rfi!.Id, Reviewer, Convert.ToBase64String([1]), ""), CancellationToken.None));
        Assert.Empty(repo.Histories);
        Assert.Equal(ProcessStatus.Draft, repo.Process!.Status);
    }

    [Fact]
    public async Task SaveFailure_DoesNotCommitTransaction()
    {
        var repo = new FakeRepo { Object = new(ObjectId, true) };
        var unit = new FakeUnit { FailSave = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateRfiCommandHandler(
            User(ApplicationRoles.Inspector), new Clock(), repo, new Sequence(1), unit)
            .Handle(new(ObjectId, "S", "Q"), CancellationToken.None));
        Assert.False(unit.Committed);
    }

    [Theory]
    [InlineData(ApplicationRoles.Inspector, false, true, false)]
    [InlineData(ApplicationRoles.Reviewer, false, false, true)]
    [InlineData(ApplicationRoles.Manager, true, false, false)]
    [InlineData(ApplicationRoles.Administrator, true, false, false)]
    public async Task List_BuildsExpectedReadScope(string role, bool full, bool inspector, bool reviewer)
    {
        var read = new FakeRead();
        await new GetRfisQueryHandler(User(role), new Clock(), read).Handle(new(), CancellationToken.None);
        Assert.Equal((full, inspector, reviewer),
            (read.Criteria!.Scope.CanReadAll, read.Criteria.Scope.IsInspector, read.Criteria.Scope.IsReviewer));
    }

    [Fact]
    public async Task List_AnonymousFailsClosed()
    {
        var anonymous = new FakeUser(null, []);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            new GetRfisQueryHandler(anonymous, new Clock(), new FakeRead()).Handle(new(), CancellationToken.None));
    }

    [Fact]
    public async Task ResponderLookup_AllowsOnlyInspectorOrManager()
    {
        var service = new Reviewers();
        await new GetRfiResponderOptionsQueryHandler(User(ApplicationRoles.Manager), service)
            .Handle(new(), CancellationToken.None);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new GetRfiResponderOptionsQueryHandler(
            User(ApplicationRoles.Administrator), service).Handle(new(), CancellationToken.None));
    }

    [Fact]
    public async Task OversizedUtf8FailsWith413PathBeforeTransaction()
    {
        var text = new string('é', RfiRules.MaximumContentUtf8Bytes / 2 + 1);
        var error = await Assert.ThrowsAsync<PayloadTooLargeException>(() => new CreateRfiCommandHandler(
            User(ApplicationRoles.Inspector), new Clock(), new FakeRepo(), new Sequence(1), new FakeUnit())
            .Handle(new(ObjectId, "S", text), CancellationToken.None));
        Assert.Equal("rfi.payload_too_large", error.Code);
    }

    private static FakeRepo Ready()
    {
        var rfi = Rfi.Create(Guid.NewGuid(), "RFI-2026-000001", ObjectId, "S", "Q", Actor, Now);
        return new FakeRepo { Rfi = rfi, Process = BusinessProcess.CreateForRfi(Guid.NewGuid(), rfi.Id, Actor, Now) };
    }
    private static ICurrentUser User(string role) => new FakeUser(Actor, [role]);
    private sealed record FakeUser(Guid? UserId, IReadOnlyCollection<string> Roles) : ICurrentUser
    { public bool IsInRole(string role) => Roles.Contains(role); }
    private sealed class Clock : IClock { public DateTime UtcNow => Now; }
    private sealed class Sequence(long value) : IRfiNumberSequence { public Task<long> NextAsync(CancellationToken ct) => Task.FromResult(value); }
    private sealed class Reviewers : IRfiReviewerService
    {
        public bool HasRole { get; init; } = true;
        public Task<RfiReviewerEligibility?> GetForAssignmentAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<RfiReviewerEligibility?>(new(id, "Reviewer", true, HasRole));
        public Task<PagedResult<RfiResponderOptionDto>> GetOptionsAsync(int p, int s, string? q, CancellationToken ct) =>
            Task.FromResult(new PagedResult<RfiResponderOptionDto>([new(Reviewer, "Reviewer")], p, s, 1, 1));
    }
    private sealed class FakeUnit : IUnitOfWork
    {
        public bool Committed { get; private set; }
        public bool FailSave { get; init; }
        public Task SaveChangesAsync(CancellationToken ct) => FailSave
            ? Task.FromException(new InvalidOperationException("save failed")) : Task.CompletedTask;
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        { var value = await operation(ct); Committed = true; return value; }
    }
    private sealed class FakeRepo : IRfiRepository
    {
        public RfiObjectState? Object { get; set; }
        public Rfi? Rfi { get; set; }
        public BusinessProcess? Process { get; set; }
        public List<ProcessHistory> Histories { get; } = [];
        public Task<RfiObjectState?> GetObjectForUpdateAsync(Guid id, CancellationToken ct) => Task.FromResult(Object);
        public Task<RfiWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<RfiWriteAggregate?>(Rfi is null || Process is null ? null : new(Rfi, Process, []));
        public void AddRfi(Rfi rfi) => Rfi = rfi;
        public void AddEntry(RfiEntry entry) { }
        public void AddFoundation(BusinessProcess process, ProcessHistory history) { Process = process; Histories.Add(history); }
        public void AddHistory(ProcessHistory history) => Histories.Add(history);
    }

    private sealed class FakeRead : IRfiReadService
    {
        public RfiQueryCriteria? Criteria { get; private set; }
        public Task<PagedResult<RfiListItemDto>> GetPageAsync(RfiQueryCriteria criteria, CancellationToken ct)
        { Criteria = criteria; return Task.FromResult(new PagedResult<RfiListItemDto>([], 1, 20, 0, 0)); }
        public Task<RfiDetailDto?> GetDetailAsync(Guid id, RfiReadScope scope, CancellationToken ct) => Task.FromResult<RfiDetailDto?>(null);
        public Task<PagedResult<RfiHistoryDto>?> GetHistoryAsync(Guid id, int page, int size, RfiReadScope scope, CancellationToken ct) => Task.FromResult<PagedResult<RfiHistoryDto>?>(null);
    }
}
