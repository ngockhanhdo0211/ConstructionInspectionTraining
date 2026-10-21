using ConstructionInspection.Application.Checklists;
using ConstructionInspection.Application.Checklists.Commands;
using ConstructionInspection.Application.Checklists.Interfaces;
using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Checklists.Queries;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.UnitTests.Checklists;

public sealed class ChecklistApplicationTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Reviewer = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid ObjectId = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly DateTime Now = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_AllocatesServerNumberAndChecklistProcessAtomically()
    {
        var repository = new FakeRepository { Object = new(ObjectId, true) };
        var unit = new FakeUnitOfWork();
        var handler = new CreateChecklistCommandHandler(User(ApplicationRoles.Inspector), new FakeClock(),
            repository, new FakeSequence(12), unit);

        var result = await handler.Handle(new(ObjectId, " Safety ", null,
            [new("Criterion", true)]), CancellationToken.None);

        Assert.Equal("CHK-2026-000012", repository.Checklist!.Number);
        Assert.Equal(Actor, repository.Checklist.CreatedByUserId);
        Assert.Equal(1, repository.Checklist.NextSubmissionNumber);
        Assert.Null(repository.Checklist.CurrentSubmissionId);
        Assert.Equal(ProcessEntityType.Checklist, repository.Process!.EntityType);
        Assert.Equal(repository.Checklist.Id, repository.Process.ChecklistId);
        Assert.Equal("Create", repository.Histories.Single().Action);
        Assert.Equal(ChecklistStatus.Draft, result.Status);
        Assert.Equal(1, unit.TransactionCount);
        Assert.Equal(1, unit.SaveCount);
        Assert.True(unit.Committed);
    }

    [Fact]
    public async Task Create_InactiveObjectReturnsConflict()
    {
        var repository = new FakeRepository { Object = new(ObjectId, false) };
        var error = await Assert.ThrowsAsync<ConflictException>(() => new CreateChecklistCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeSequence(1), new FakeUnitOfWork())
            .Handle(new(ObjectId, "Title", null, [new("Criterion", true)]), CancellationToken.None));
        Assert.Equal("checklist.object_inactive", error.Code);
        Assert.Null(repository.Checklist);
    }

    [Theory]
    [InlineData(ApplicationRoles.Reviewer)]
    [InlineData(ApplicationRoles.Manager)]
    [InlineData(ApplicationRoles.Administrator)]
    public async Task Create_NonInspectorIsForbidden(string role) =>
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => new CreateChecklistCommandHandler(
            User(role), new FakeClock(), new FakeRepository(), new FakeSequence(1), new FakeUnitOfWork())
            .Handle(new(ObjectId, "Title", null, [new("Criterion", true)]), CancellationToken.None));

    [Fact]
    public async Task Create_AnonymousFailsClosed() =>
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => new CreateChecklistCommandHandler(
            new FakeUser(null, []), new FakeClock(), new FakeRepository(), new FakeSequence(1), new FakeUnitOfWork())
            .Handle(new(ObjectId, "Title", null, [new("Criterion", true)]), CancellationToken.None));

    [Fact]
    public async Task Start_DraftTransitionsAndWritesStartHistory()
    {
        var repository = ReadyRepository(ProcessStatus.Draft);
        var result = await new StartChecklistCommandHandler(User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, "", ""), CancellationToken.None);
        Assert.Equal(ChecklistStatus.InProgress, result.Status);
        Assert.Equal("Start", repository.Histories.Single().Action);
    }

    [Fact]
    public async Task Start_RecheckResumesWithoutCheckingObjectActive()
    {
        var repository = ReadyRepository(ProcessStatus.Recheck);
        repository.Object = new(ObjectId, false);
        var result = await new StartChecklistCommandHandler(User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, "", ""), CancellationToken.None);
        Assert.Equal(ChecklistStatus.InProgress, result.Status);
        Assert.Equal("Resume", repository.Histories.Single().Action);
        Assert.Equal(0, repository.ObjectLoadCount);
    }

    [Fact]
    public async Task Start_NonOwnerIsHiddenAsNotFound()
    {
        var repository = ReadyRepository(ProcessStatus.Draft);
        repository.Checklist = CreateChecklist(Guid.NewGuid());
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new StartChecklistCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist.Id, "", ""), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateItems_ReconcilesAddRemoveAndCanonicalOrder()
    {
        var repository = ReadyRepository(ProcessStatus.Draft);
        var existing = repository.Items.Single();
        await new UpdateChecklistItemsCommandHandler(User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, "", "",
            [new(null, null, "New first", true, ChecklistItemResult.NotChecked, null),
             new(existing.Id, Convert.ToBase64String(existing.RowVersion), existing.Criterion, existing.IsRequired, ChecklistItemResult.Pass, null)]), CancellationToken.None);
        Assert.Single(repository.AddedItems);
        Assert.Equal(2, repository.Checklist.Items.Count);
        Assert.Equal([1, 2], repository.Checklist.Items.Select(x => x.Sequence));
    }

    [Fact]
    public async Task UpdateItems_StaleChildRollsBackWholeTransaction()
    {
        var repository = ReadyRepository(ProcessStatus.Draft);
        var item = repository.Items.Single();
        var unit = new FakeUnitOfWork();
        await Assert.ThrowsAsync<ConflictException>(() => new UpdateChecklistItemsCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, unit)
            .Handle(new(repository.Checklist!.Id, "", "",
                [new(item.Id, Convert.ToBase64String([2]), item.Criterion, true, ChecklistItemResult.Pass, null)]), CancellationToken.None));
        Assert.False(unit.Committed);
        Assert.Equal(0, unit.SaveCount);
    }

    [Fact]
    public async Task UpdateItems_AfterResumeRejectsStructuralChange()
    {
        var repository = ReadyRepository(ProcessStatus.Recheck, ChecklistItemResult.Pass);
        repository.Process!.ResumeChecklist(Now);
        var item = repository.Items.Single();
        var error = await Assert.ThrowsAsync<ConflictException>(() => new UpdateChecklistItemsCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, "", "",
                [new(item.Id, Convert.ToBase64String(item.RowVersion), "Changed", true, ChecklistItemResult.Pass, null)]), CancellationToken.None));
        Assert.Equal("checklist.structure_immutable", error.Code);
    }

    [Fact]
    public async Task ReplaceLinks_RejectsCrossObjectAndLeavesExistingSet()
    {
        var repository = ReadyRepository(ProcessStatus.InProgress);
        var inspectionId = Guid.NewGuid();
        repository.Inspections = [new(inspectionId, Guid.NewGuid(), Actor)];
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new ReplaceChecklistInspectionsCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, [inspectionId], "", ""), CancellationToken.None));
        Assert.Equal(0, repository.ReplaceLinksCount);
    }

    [Fact]
    public async Task ReplaceLinks_InactiveObjectIsConflict()
    {
        var repository = ReadyRepository(ProcessStatus.InProgress);
        repository.Object = new(ObjectId, false);
        var error = await Assert.ThrowsAsync<ConflictException>(() => new ReplaceChecklistInspectionsCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, [], "", ""), CancellationToken.None));
        Assert.Equal("checklist.object_inactive", error.Code);
    }

    [Fact]
    public async Task Submit_CapturesItemsAndLinksAndUsesTwoSavesInOneTransaction()
    {
        var repository = ReadyRepository(ProcessStatus.InProgress, ChecklistItemResult.Pass);
        var linkedId = Guid.NewGuid();
        repository.Links.Add(ChecklistInspection.Create(repository.Checklist!.Id, linkedId, Actor, Now));
        var unit = new FakeUnitOfWork();
        var result = await new SubmitChecklistCommandHandler(User(ApplicationRoles.Inspector), new FakeClock(),
            repository, EligibleReviewers(), unit).Handle(new(repository.Checklist.Id, Reviewer, "", ""), CancellationToken.None);
        Assert.Equal(ChecklistStatus.Submitted, result.Status);
        Assert.Equal(1, repository.Submission!.SubmissionNumber);
        Assert.Single(repository.SubmissionItems);
        Assert.Equal(linkedId, repository.SubmissionLinks.Single().InspectionId);
        Assert.Equal(2, repository.Checklist.NextSubmissionNumber);
        Assert.Equal(repository.Submission.Id, repository.Checklist.CurrentSubmissionId);
        Assert.Equal(2, unit.SaveCount);
        Assert.True(unit.Committed);
        Assert.Equal("Submit", repository.Histories.Single().Action);
    }

    [Fact]
    public async Task Submit_FailWithoutInspectionIsConflict()
    {
        var repository = ReadyRepository(ProcessStatus.InProgress, ChecklistItemResult.Fail, "Defect");
        var error = await Assert.ThrowsAsync<ConflictException>(() => new SubmitChecklistCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, EligibleReviewers(), new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, Reviewer, "", ""), CancellationToken.None));
        Assert.Equal("checklist.inspection_required", error.Code);
        Assert.Null(repository.Submission);
    }

    [Fact]
    public async Task Submit_InvalidReviewerDoesNotCreateSnapshot()
    {
        var repository = ReadyRepository(ProcessStatus.InProgress, ChecklistItemResult.Pass);
        var reviewers = new FakeReviewerService { Eligibility = new(Reviewer, "Reviewer", false, true) };
        await Assert.ThrowsAsync<ConflictException>(() => new SubmitChecklistCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, reviewers, new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, Reviewer, "", ""), CancellationToken.None));
        Assert.Null(repository.Submission);
    }

    [Fact]
    public async Task Submit_SecondSaveFailureDoesNotCommitTransaction()
    {
        var repository = ReadyRepository(ProcessStatus.InProgress, ChecklistItemResult.Pass);
        var unit = new FakeUnitOfWork { ThrowOnSaveCall = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SubmitChecklistCommandHandler(
            User(ApplicationRoles.Inspector), new FakeClock(), repository, EligibleReviewers(), unit)
            .Handle(new(repository.Checklist!.Id, Reviewer, "", ""), CancellationToken.None));
        Assert.Equal(2, unit.SaveCount);
        Assert.False(unit.Committed);
    }

    [Fact]
    public async Task ReviewerWorkflow_AssignedReviewerCanStartAndApprove()
    {
        var repository = ReadyRepository(ProcessStatus.Submitted, ChecklistItemResult.Pass, allocateSubmission: true);
        var unit = new FakeUnitOfWork();
        var start = await new StartChecklistReviewCommandHandler(User(ApplicationRoles.Reviewer, Reviewer), new FakeClock(),
            repository, EligibleReviewers(), unit).Handle(new(repository.Checklist!.Id, ""), CancellationToken.None);
        Assert.Equal(ChecklistStatus.UnderReview, start.Status);
        var approve = await new ApproveChecklistCommandHandler(User(ApplicationRoles.Reviewer, Reviewer), new FakeClock(),
            repository, EligibleReviewers(), unit).Handle(new(repository.Checklist.Id, "ok", ""), CancellationToken.None);
        Assert.Equal(ChecklistStatus.Approved, approve.Status);
        Assert.Equal(["StartReview", "Approve"], repository.Histories.Select(x => x.Action));
    }

    [Fact]
    public async Task ReviewerWorkflow_OtherReviewerIsHidden()
    {
        var repository = ReadyRepository(ProcessStatus.Submitted, ChecklistItemResult.Pass, allocateSubmission: true);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new StartChecklistReviewCommandHandler(
            User(ApplicationRoles.Reviewer, Guid.NewGuid()), new FakeClock(), repository, EligibleReviewers(), new FakeUnitOfWork())
            .Handle(new(repository.Checklist!.Id, ""), CancellationToken.None));
    }

    [Theory]
    [InlineData(ApplicationRoles.Manager, true)]
    [InlineData(ApplicationRoles.Administrator, true)]
    [InlineData(ApplicationRoles.Inspector, false)]
    [InlineData(ApplicationRoles.Reviewer, false)]
    public async Task ListQuery_PassesExpectedReadScope(string role, bool fullRead)
    {
        var read = new FakeReadService();
        await new GetChecklistsQueryHandler(User(role), read).Handle(new(), CancellationToken.None);
        Assert.Equal(fullRead, read.Criteria!.Scope.CanReadAll);
        Assert.Equal(role == ApplicationRoles.Inspector, read.Criteria.Scope.IsInspector);
        Assert.Equal(role == ApplicationRoles.Reviewer, read.Criteria.Scope.IsReviewer);
    }

    [Fact]
    public async Task DetailQuery_PropagatesCancellationAndNotFound()
    {
        var read = new FakeReadService();
        using var source = new CancellationTokenSource();
        var error = await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetChecklistDetailQueryHandler(
            User(ApplicationRoles.Manager), read).Handle(new(Guid.NewGuid()), source.Token));
        Assert.Equal("checklist.not_found", error.Code);
        Assert.Equal(source.Token, read.Token);
    }

    [Fact]
    public void Validators_RejectInvalidPagingReasonAndSubmissionNumber()
    {
        Assert.False(new GetChecklistsQueryValidator().Validate(new GetChecklistsQuery(PageNumber: 0)).IsValid);
        Assert.False(new RejectChecklistCommandValidator().Validate(
            new RejectChecklistCommand(Guid.NewGuid(), " ", "token")).IsValid);
        Assert.False(new GetChecklistSubmissionQueryValidator().Validate(
            new GetChecklistSubmissionQuery(Guid.NewGuid(), 0)).IsValid);
    }

    [Fact]
    public async Task HistoryAndSubmissionQueries_PropagateScopeAndCancellation()
    {
        var read = new FakeReadService();
        var id = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetChecklistHistoryQueryHandler(
            User(ApplicationRoles.Administrator), read).Handle(new(id), source.Token));
        Assert.Equal("history", read.LastOperation);
        Assert.True(read.LastScope!.CanReadAll);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetChecklistSubmissionsQueryHandler(
            User(ApplicationRoles.Manager), read).Handle(new(id), source.Token));
        Assert.Equal("submissions", read.LastOperation);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetChecklistSubmissionQueryHandler(
            User(ApplicationRoles.Inspector), read).Handle(new(id, 1), source.Token));
        Assert.Equal("submission", read.LastOperation);
        Assert.Equal(source.Token, read.Token);
    }

    private static FakeUser User(string role, Guid? id = null) => new(id ?? Actor, [role]);
    private static FakeReviewerService EligibleReviewers() => new() { Eligibility = new(Reviewer, "Reviewer", true, true) };

    private static Checklist CreateChecklist(Guid actorId = default)
    {
        if (actorId == default) actorId = Actor;
        return Checklist.Create(Guid.NewGuid(), "CHK-2026-000001", ObjectId, "Checklist", null,
            actorId, Now, [("Criterion", true)]);
    }

    private static FakeRepository ReadyRepository(ProcessStatus status,
        ChecklistItemResult result = ChecklistItemResult.NotChecked, string? note = null, bool allocateSubmission = false)
    {
        var checklist = CreateChecklist();
        var item = checklist.Items.Single();
        typeof(ChecklistItem).GetProperty(nameof(ChecklistItem.RowVersion))!.SetValue(item, new byte[] { 1 });
        if (result != ChecklistItemResult.NotChecked) item.UpdateResult(result, note, Actor, Now);
        var process = BusinessProcess.CreateForChecklist(Guid.NewGuid(), checklist.Id, Actor, Now);
        if (status != ProcessStatus.Draft) process.StartChecklist(Now);
        if (status is ProcessStatus.Submitted or ProcessStatus.UnderReview or ProcessStatus.Approved or ProcessStatus.Recheck or ProcessStatus.Rejected)
        {
            if (result == ChecklistItemResult.NotChecked) item.UpdateResult(ChecklistItemResult.Pass, null, Actor, Now);
            var submissionId = Guid.NewGuid();
            checklist.AllocateSubmission(submissionId, Reviewer, Actor, Now);
            process.SubmitChecklist(Reviewer, Now);
        }
        if (status is ProcessStatus.UnderReview or ProcessStatus.Approved or ProcessStatus.Recheck or ProcessStatus.Rejected)
            process.StartChecklistReview(Reviewer, Now);
        if (status == ProcessStatus.Approved) process.ApproveChecklist(Reviewer, Now);
        if (status == ProcessStatus.Recheck) process.RequestChecklistRecheck(Reviewer, "Fix", Now);
        if (status == ProcessStatus.Rejected) process.RejectChecklist(Reviewer, "Reject", Now);
        return new FakeRepository
        {
            Checklist = checklist,
            Process = process,
            Items = [item],
            Object = new(ObjectId, true),
            Submission = allocateSubmission && checklist.CurrentSubmissionId.HasValue
                ? ChecklistSubmission.Create(checklist.CurrentSubmissionId.Value, checklist.Id, 1, Actor, Now)
                : null
        };
    }

    private sealed class FakeUser(Guid? id, IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public Guid? UserId => id;
        public IReadOnlyCollection<string> Roles => roles;
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.Ordinal);
    }
    private sealed class FakeClock : IClock { public DateTime UtcNow => Now; }
    private sealed class FakeSequence(long value) : IChecklistNumberSequence
    {
        public Task<long> NextAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int TransactionCount { get; private set; }
        public int SaveCount { get; private set; }
        public bool Committed { get; private set; }
        public int? ThrowOnSaveCall { get; init; }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            if (ThrowOnSaveCall == SaveCount) throw new InvalidOperationException("Simulated persistence failure.");
            return Task.CompletedTask;
        }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            TransactionCount++;
            var result = await operation(cancellationToken);
            Committed = true;
            return result;
        }
    }
    private sealed class FakeReviewerService : IInspectionReviewerService
    {
        public ReviewerEligibility? Eligibility { get; set; }
        public Task<ReviewerEligibility?> GetForAssignmentAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Eligibility?.UserId == userId ? Eligibility : null);
        public Task<PagedResult<ReviewerOptionDto>> GetOptionsAsync(ReviewerOptionCriteria criteria, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<ReviewerOptionDto>([], 1, 20, 0, 0));
    }
    private sealed class FakeRepository : IChecklistRepository
    {
        public ChecklistObjectState? Object { get; set; }
        public int ObjectLoadCount { get; private set; }
        public Checklist? Checklist { get; set; }
        public BusinessProcess? Process { get; set; }
        public List<ChecklistItem> Items { get; set; } = [];
        public List<ChecklistInspection> Links { get; } = [];
        public IReadOnlyList<ChecklistInspectionState> Inspections { get; set; } = [];
        public List<ChecklistItem> AddedItems { get; } = [];
        public int ReplaceLinksCount { get; private set; }
        public ChecklistSubmission? Submission { get; set; }
        public List<ChecklistSubmissionItem> SubmissionItems { get; } = [];
        public List<ChecklistSubmissionInspection> SubmissionLinks { get; } = [];
        public List<ProcessHistory> Histories { get; } = [];
        public Task<ChecklistObjectState?> GetObjectForUpdateAsync(Guid id, CancellationToken cancellationToken)
        { ObjectLoadCount++; return Task.FromResult(Object); }
        public Task<ChecklistWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Checklist is null || Process is null ? null : new ChecklistWriteAggregate(Checklist, Items, Links, Process));
        public Task<IReadOnlyList<ChecklistInspectionState>> GetInspectionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult(Inspections);
        public void AddChecklist(Checklist checklist) => Checklist = checklist;
        public void AddItems(IEnumerable<ChecklistItem> items) { var values = items.ToArray(); AddedItems.AddRange(values); Items.AddRange(values.Where(x => !Items.Contains(x))); }
        public void RemoveItems(IEnumerable<ChecklistItem> items) { foreach (var item in items.ToArray()) Items.Remove(item); }
        public void ReplaceLinks(IEnumerable<ChecklistInspection> current, IEnumerable<ChecklistInspection> replacement)
        { ReplaceLinksCount++; Links.Clear(); Links.AddRange(replacement); }
        public void AddFoundation(BusinessProcess process, ProcessHistory history) { Process = process; Histories.Add(history); }
        public void AddSubmission(ChecklistSubmission submission, IEnumerable<ChecklistSubmissionItem> items,
            IEnumerable<ChecklistSubmissionInspection> inspections)
        { Submission = submission; SubmissionItems.AddRange(items); SubmissionLinks.AddRange(inspections); }
        public void AddHistory(ProcessHistory history) => Histories.Add(history);
    }
    private sealed class FakeReadService : IChecklistReadService
    {
        public ChecklistQueryCriteria? Criteria { get; private set; }
        public CancellationToken Token { get; private set; }
        public string? LastOperation { get; private set; }
        public ChecklistReadScope? LastScope { get; private set; }
        public Task<PagedResult<ChecklistListItemDto>> GetPageAsync(ChecklistQueryCriteria criteria, CancellationToken cancellationToken)
        { Criteria = criteria; Token = cancellationToken; return Task.FromResult(new PagedResult<ChecklistListItemDto>([], 1, 20, 0, 0)); }
        public Task<ChecklistDetailDto?> GetDetailAsync(Guid id, ChecklistReadScope scope, CancellationToken cancellationToken)
        { Token = cancellationToken; return Task.FromResult<ChecklistDetailDto?>(null); }
        public Task<PagedResult<ChecklistHistoryDto>?> GetHistoryAsync(Guid id, int pageNumber, int pageSize, ChecklistReadScope scope, CancellationToken cancellationToken)
        { LastOperation = "history"; LastScope = scope; Token = cancellationToken; return Task.FromResult<PagedResult<ChecklistHistoryDto>?>(null); }
        public Task<PagedResult<ChecklistSubmissionListItemDto>?> GetSubmissionsAsync(Guid id, int pageNumber, int pageSize, ChecklistReadScope scope, CancellationToken cancellationToken)
        { LastOperation = "submissions"; LastScope = scope; Token = cancellationToken; return Task.FromResult<PagedResult<ChecklistSubmissionListItemDto>?>(null); }
        public Task<ChecklistSubmissionDetailDto?> GetSubmissionAsync(Guid id, int submissionNumber, ChecklistReadScope scope, CancellationToken cancellationToken)
        { LastOperation = "submission"; LastScope = scope; Token = cancellationToken; return Task.FromResult<ChecklistSubmissionDetailDto?>(null); }
    }
}
