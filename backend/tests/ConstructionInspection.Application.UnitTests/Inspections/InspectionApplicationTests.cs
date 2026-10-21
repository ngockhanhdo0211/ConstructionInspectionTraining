using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.FormTemplates.Validation;
using ConstructionInspection.Application.Inspections;
using ConstructionInspection.Application.Inspections.Commands;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Application.Inspections.Queries;
using ConstructionInspection.Application.Inspections.Validation;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.UnitTests.Inspections;

public sealed class InspectionApplicationTests
{
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ObjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TemplateId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid VersionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ReviewerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTime Now = new(2026, 10, 17, 2, 0, 0, DateTimeKind.Utc);
    private const string Schema = "{\"components\":[{\"type\":\"checkbox\",\"key\":\"ok\",\"label\":\"OK\"}]}";

    [Fact]
    public async Task Create_AsInspector_CreatesCompleteAtomicFoundation()
    {
        var repository = ReadyRepository();
        var unit = new FakeUnitOfWork();
        var handler = CreateHandler(repository, unit: unit);

        var result = await handler.Handle(
            new(ObjectId, TemplateId, "Inspection", null, "{\"ok\":true}"), CancellationToken.None);

        Assert.NotNull(repository.Inspection);
        Assert.Equal(VersionId, repository.Inspection.FormTemplateVersionId);
        Assert.Null(repository.Inspection.ReviewerUserId);
        Assert.Equal(2, repository.Inspection.NextRevisionNumber);
        Assert.Equal(1, repository.Submission!.RevisionNumber);
        Assert.Equal(ProcessStatus.Draft, repository.Process!.Status);
        Assert.Equal("Create", repository.History!.Action);
        Assert.Equal(1, unit.TransactionCount);
        Assert.Equal(2, unit.SaveCount);
        Assert.Equal(repository.Inspection.Id, result.Id);
    }

    [Fact]
    public async Task Create_NonInspector_IsForbiddenBeforeRepositoryCall()
    {
        var repository = ReadyRepository();
        var handler = CreateHandler(repository, new FakeUser(Actor, [ApplicationRoles.Administrator]));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(
            new(ObjectId, TemplateId, "Inspection", null, "{}"), CancellationToken.None));
        Assert.Null(repository.Inspection);
    }

    [Fact]
    public async Task Create_InactiveObject_IsConflict()
    {
        var repository = ReadyRepository();
        repository.Object = new(ObjectId, false);
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(repository).Handle(
            new(ObjectId, TemplateId, "Inspection", null, "{}"), CancellationToken.None));
        Assert.Equal("inspection.construction_object_inactive", error.Code);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Create_UnavailableTemplate_IsConflict(bool active, bool hasPublished)
    {
        var repository = ReadyRepository();
        repository.Template = new(TemplateId, active,
            hasPublished ? VersionId : null, hasPublished ? 2 : null, hasPublished ? Schema : null);
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(repository).Handle(
            new(ObjectId, TemplateId, "Inspection", null, "{}"), CancellationToken.None));
        Assert.Equal("inspection.form_template_unavailable", error.Code);
    }

    [Fact]
    public async Task SaveDraft_UsesPinnedVersionAndUpdatesRevisionOne()
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var handler = new SaveInspectionDraftCommandHandler(
            Inspector(), new FakeClock(), repository, Validator(), new FakeUnitOfWork());

        var result = await handler.Handle(new(
            repository.Inspection!.Id, "{\"ok\":true}", "", "", ""), CancellationToken.None);

        Assert.Equal(VersionId, repository.PinnedVersionRequested);
        Assert.Equal(1, result.RevisionNumber);
        Assert.Equal("{\"ok\":true}", result.DataJson);
    }

    [Fact]
    public async Task SaveDraft_NonOwner_IsForbidden()
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var handler = new SaveInspectionDraftCommandHandler(
            new FakeUser(Guid.NewGuid(), [ApplicationRoles.Inspector]), new FakeClock(),
            repository, Validator(), new FakeUnitOfWork());
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.Handle(new(
            repository.Inspection!.Id, "{}", "", "", ""), CancellationToken.None));
    }

    [Fact]
    public async Task SaveDraft_StaleRowVersion_IsConflictWithoutRetry()
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var unit = new FakeUnitOfWork();
        var handler = new SaveInspectionDraftCommandHandler(
            Inspector(), new FakeClock(), repository, Validator(), unit);

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(
            repository.Inspection!.Id, "{}", "AA==", "", ""), CancellationToken.None));

        Assert.Equal("inspection.concurrency_conflict", error.Code);
        Assert.Equal(1, unit.TransactionCount);
        Assert.Equal(0, unit.SaveCount);
    }

    [Fact]
    public void DataValidator_AcceptsAllSevenTypesAndCanonicalizes()
    {
        const string schema = """
        {"components":[
          {"type":"textfield","key":"text","label":"Text"},
          {"type":"textarea","key":"notes","label":"Notes"},
          {"type":"number","key":"qty","label":"Qty","validate":{"min":1,"max":5,"integer":true}},
          {"type":"select","key":"trade","label":"Trade","dataSrc":"values","data":{"values":[{"label":"Civil","value":"civil"}]}},
          {"type":"radio","key":"result","label":"Result","values":[{"label":"Pass","value":"pass"}]},
          {"type":"checkbox","key":"ok","label":"OK"},
          {"type":"datetime","key":"at","label":"At","enableDate":true,"enableTime":true}
        ]}
        """;
        const string data = """
        {"text":"a","notes":"b","qty":2,"trade":"civil","result":"pass","ok":true,"at":"2026-10-17T02:00:00Z"}
        """;
        var result = Validator().ValidateDraft(schema, data);
        Assert.DoesNotContain("\r", result.CanonicalJson);
    }

    [Theory]
    [InlineData("{\"unknown\":1}")]
    [InlineData("{\"ok\":\"true\"}")]
    [InlineData("{\"ok\":true,\"OK\":false}")]
    [InlineData("{\"ok\":\"<script>alert(1)</script>\"}")]
    public void DataValidator_RejectsUnknownDuplicateWrongTypeOrExecutable(string data)
    {
        var error = Assert.Throws<BadRequestException>(() => Validator().ValidateDraft(Schema, data));
        Assert.Equal("inspection.form_data_invalid", error.Code);
    }

    [Fact]
    public void DataValidator_DraftAllowsMissingOptionalOrRequiredField()
    {
        const string requiredSchema = "{\"components\":[{\"type\":\"textfield\",\"key\":\"name\",\"label\":\"Name\",\"validate\":{\"required\":true}}]}";
        Assert.Equal("{}", Validator().ValidateDraft(requiredSchema, "{}").CanonicalJson);
    }

    [Fact]
    public void DataValidator_SubmitRequiresConfiguredRequiredField()
    {
        const string requiredSchema = "{\"components\":[{\"type\":\"textfield\",\"key\":\"name\",\"label\":\"Name\",\"validate\":{\"required\":true}}]}";
        var error = Assert.Throws<BadRequestException>(() =>
            Validator().ValidateSubmit(requiredSchema, "{}"));
        Assert.Equal("inspection.form_data_invalid", error.Code);
        Assert.Equal("{\"name\":\"Ready\"}",
            Validator().ValidateSubmit(requiredSchema, "{\"name\":\"Ready\"}").CanonicalJson);
    }

    [Fact]
    public async Task Submit_AssignsEligibleReviewer_ValidatesPinnedSchemaAndWritesHistory()
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var reviewers = EligibleReviewers();
        var unit = new FakeUnitOfWork();
        var handler = new SubmitInspectionCommandHandler(
            Inspector(), new FakeClock(), repository, reviewers, Validator(), unit);

        var result = await handler.Handle(new(
            repository.Inspection!.Id, ReviewerId, "", "", ""), CancellationToken.None);

        Assert.Equal(InspectionStatus.Submitted, result.Status);
        Assert.Equal(ReviewerId, repository.Inspection.ReviewerUserId);
        Assert.Equal(ReviewerId, repository.Process!.AssignedToUserId);
        Assert.NotNull(repository.Submission!.SubmittedAt);
        Assert.Equal("Submit", repository.History!.Action);
        Assert.Equal(ReviewerId, repository.History.NewAssignedToUserId);
        Assert.Equal(1, unit.TransactionCount);
        Assert.Equal(1, unit.SaveCount);
    }

    [Fact]
    public async Task Submit_IneligibleReviewer_IsConflictInsideTransaction()
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var reviewers = new FakeReviewerService
        {
            Reviewer = new(ReviewerId, "Disabled", false, true)
        };
        var unit = new FakeUnitOfWork();
        var handler = new SubmitInspectionCommandHandler(
            Inspector(), new FakeClock(), repository, reviewers, Validator(), unit);

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(
            repository.Inspection!.Id, ReviewerId, "", "", ""), CancellationToken.None));

        Assert.Equal("inspection.reviewer_invalid", error.Code);
        Assert.Equal(1, unit.TransactionCount);
        Assert.Equal(0, unit.SaveCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("role-removed")]
    [InlineData("self-review")]
    public async Task Submit_RejectsEveryReviewerEligibilityFailure(string scenario)
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var reviewerId = scenario == "self-review" ? Actor : ReviewerId;
        var reviewers = new FakeReviewerService
        {
            Reviewer = scenario switch
            {
                "missing" => null,
                "role-removed" => new(ReviewerId, "Reviewer", true, false),
                _ => new(Actor, "Creator", true, true)
            }
        };
        var handler = new SubmitInspectionCommandHandler(
            Inspector(), new FakeClock(), repository, reviewers, Validator(), new FakeUnitOfWork());

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(
            repository.Inspection!.Id, reviewerId, "", "", ""), CancellationToken.None));

        Assert.Equal(
            scenario == "self-review" ? "inspection.self_review_forbidden" : "inspection.reviewer_invalid",
            error.Code);
    }

    [Theory]
    [InlineData("inspection")]
    [InlineData("submission")]
    [InlineData("process")]
    public async Task Submit_RejectsEachStaleAggregateRowVersion(string staleToken)
    {
        var repository = ReadyRepository();
        SeedAggregate(repository);
        var handler = new SubmitInspectionCommandHandler(
            Inspector(), new FakeClock(), repository, EligibleReviewers(), Validator(), new FakeUnitOfWork());

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(
            repository.Inspection!.Id,
            ReviewerId,
            staleToken == "inspection" ? "AA==" : "",
            staleToken == "submission" ? "AA==" : "",
            staleToken == "process" ? "AA==" : ""), CancellationToken.None));

        Assert.Equal("inspection.concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task ReviewerActions_RequireStartReviewAndReachRecheck()
    {
        var repository = ReadyRepository();
        SeedSubmitted(repository);
        var reviewers = EligibleReviewers();
        var reviewer = new FakeUser(ReviewerId, [ApplicationRoles.Reviewer]);

        var premature = new ApproveInspectionCommandHandler(
            reviewer, new FakeClock(), repository, reviewers, new FakeUnitOfWork());
        Assert.Equal("inspection.invalid_state", (await Assert.ThrowsAsync<InspectionDomainException>(() =>
            premature.Handle(new(repository.Inspection!.Id, null, ""), CancellationToken.None))).Code);

        await new StartInspectionReviewCommandHandler(
            reviewer, new FakeClock(), repository, reviewers, new FakeUnitOfWork())
            .Handle(new(repository.Inspection!.Id, ""), CancellationToken.None);
        await new RequestInspectionRecheckCommandHandler(
            reviewer, new FakeClock(), repository, reviewers, new FakeUnitOfWork())
            .Handle(new(repository.Inspection.Id, "Correct values", ""), CancellationToken.None);

        Assert.Equal(ProcessStatus.Recheck, repository.Process!.Status);
        Assert.Equal("RequestRecheck", repository.History!.Action);
        Assert.Equal("Correct values", repository.History.Note);
    }

    [Theory]
    [InlineData("approve", InspectionStatus.Approved, "Approve")]
    [InlineData("reject", InspectionStatus.Rejected, "Reject")]
    public async Task ReviewerDecision_CompletesAndWritesExactHistory(
        string decision, InspectionStatus expectedStatus, string expectedAction)
    {
        var repository = ReadyRepository();
        SeedSubmitted(repository);
        repository.Process!.StartReview(ReviewerId, Now);
        var reviewer = new FakeUser(ReviewerId, [ApplicationRoles.Reviewer]);
        var reviewers = EligibleReviewers();
        var unit = new FakeUnitOfWork();

        var result = decision == "approve"
            ? await new ApproveInspectionCommandHandler(
                reviewer, new FakeClock(), repository, reviewers, unit)
                .Handle(new(repository.Inspection!.Id, "Looks good", ""), CancellationToken.None)
            : await new RejectInspectionCommandHandler(
                reviewer, new FakeClock(), repository, reviewers, unit)
                .Handle(new(repository.Inspection!.Id, "Not compliant", ""), CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.NotNull(result.CompletedAt);
        Assert.Equal(expectedAction, repository.History!.Action);
        Assert.Equal(ProcessStatus.UnderReview, repository.History.FromStatus);
        Assert.Equal(expectedStatus.ToProcessStatus(), repository.History.ToStatus);
    }

    [Fact]
    public async Task Reassign_FromUnderReview_ReturnsSubmittedAndAuditsReviewers()
    {
        var repository = ReadyRepository();
        SeedSubmitted(repository);
        repository.Process!.StartReview(ReviewerId, Now);
        var replacement = Guid.NewGuid();
        var reviewers = new FakeReviewerService
        {
            Reviewer = new(replacement, "Replacement", true, true)
        };
        var handler = new ReassignInspectionReviewerCommandHandler(
            new FakeUser(Actor, [ApplicationRoles.Manager]), new FakeClock(),
            repository, reviewers, new FakeUnitOfWork());

        var result = await handler.Handle(new(
            repository.Inspection!.Id, replacement, "Coverage", "", ""), CancellationToken.None);

        Assert.Equal(InspectionStatus.Submitted, result.Status);
        Assert.Equal(ReviewerId, repository.History!.PreviousAssignedToUserId);
        Assert.Equal(replacement, repository.History.NewAssignedToUserId);
        Assert.Equal(ProcessStatus.UnderReview, repository.History.FromStatus);
        Assert.Equal(ProcessStatus.Submitted, repository.History.ToStatus);
    }

    [Fact]
    public async Task Reassign_RemovesOldReviewerObjectScope()
    {
        var repository = ReadyRepository();
        SeedSubmitted(repository);
        var replacement = Guid.NewGuid();
        var reviewers = new FakeReviewerService
        {
            Reviewer = new(replacement, "Replacement", true, true)
        };
        await new ReassignInspectionReviewerCommandHandler(
            new FakeUser(Actor, [ApplicationRoles.Administrator]), new FakeClock(),
            repository, reviewers, new FakeUnitOfWork()).Handle(new(
                repository.Inspection!.Id, replacement, "Coverage", "", ""), CancellationToken.None);

        var oldReviewerAction = new StartInspectionReviewCommandHandler(
            new FakeUser(ReviewerId, [ApplicationRoles.Reviewer]), new FakeClock(),
            repository, reviewers, new FakeUnitOfWork());

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => oldReviewerAction.Handle(
            new(repository.Inspection.Id, ""), CancellationToken.None));
    }

    [Fact]
    public async Task SaveFirstRecheck_AllocatesOneRevisionThenUpdatesItWithoutExtraHistory()
    {
        var repository = ReadyRepository();
        SeedSubmitted(repository);
        repository.Process!.StartReview(ReviewerId, Now);
        repository.Process.RequestRecheck(ReviewerId, "Correct", Now);
        var handler = new SaveInspectionDraftCommandHandler(
            Inspector(), new FakeClock(), repository, Validator(), new FakeUnitOfWork());

        var first = await handler.Handle(new(
            repository.Inspection!.Id, "{\"ok\":true}", "", "", ""), CancellationToken.None);
        var createdHistoryCount = repository.Histories.Count;
        var oldData = "{}";
        var second = await handler.Handle(new(
            repository.Inspection.Id, "{\"ok\":false}", "", "", ""), CancellationToken.None);

        Assert.Equal(2, first.RevisionNumber);
        Assert.Equal(2, second.RevisionNumber);
        Assert.Equal(3, repository.Inspection.NextRevisionNumber);
        Assert.Equal(createdHistoryCount, repository.Histories.Count);
        Assert.Equal("CreateRevision", repository.History!.Action);
        Assert.Equal(oldData, repository.Submissions.Single(x => x.RevisionNumber == 1).DataJson);
        Assert.Equal("{\"ok\":false}", repository.Submission!.DataJson);
    }

    [Fact]
    public async Task ReviewerOptions_ForwardsNormalizedCriteriaAndCancellation()
    {
        var service = EligibleReviewers();
        var handler = new GetReviewerOptionsQueryHandler(Inspector(), service);
        using var cancellation = new CancellationTokenSource();

        await handler.Handle(new(2, 5, "  review  "), cancellation.Token);

        Assert.Equal(new ReviewerOptionCriteria(2, 5, "review"), service.Criteria);
        Assert.Equal(cancellation.Token, service.CancellationToken);
    }

    [Fact]
    public async Task HistoryAndRevisionQueries_ForwardReadScopePagingAndCancellation()
    {
        var repository = ReadyRepository();
        var readService = new FakeReadService(repository);
        var historyHandler = new GetInspectionHistoryQueryHandler(Inspector(), readService);
        var revisionsHandler = new GetInspectionRevisionsQueryHandler(Inspector(), readService);
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();

        await historyHandler.Handle(new(id, 2, 5), cancellation.Token);
        await revisionsHandler.Handle(new(id, 3, 10), cancellation.Token);

        Assert.Equal(id, readService.HistoryCriteria!.InspectionId);
        Assert.Equal(2, readService.HistoryCriteria.PageNumber);
        Assert.Equal(Actor, readService.HistoryCriteria.Scope.UserId);
        Assert.Equal(id, readService.RevisionCriteria!.InspectionId);
        Assert.Equal(3, readService.RevisionCriteria.PageNumber);
        Assert.Equal(cancellation.Token, readService.WorkflowQueryCancellationToken);
    }

    [Fact]
    public void WorkflowValidators_RejectMissingReasonAndConcurrencyTokens()
    {
        Assert.False(new RejectInspectionCommandValidator().Validate(
            new RejectInspectionCommand(Guid.NewGuid(), "", "")).IsValid);
        Assert.False(new RequestInspectionRecheckCommandValidator().Validate(
            new RequestInspectionRecheckCommand(Guid.NewGuid(), " ", "")).IsValid);
        Assert.False(new ReassignInspectionReviewerCommandValidator().Validate(
            new ReassignInspectionReviewerCommand(Guid.NewGuid(), ReviewerId, "", "", "")).IsValid);
        Assert.False(new SubmitInspectionCommandValidator().Validate(
            new SubmitInspectionCommand(Guid.NewGuid(), ReviewerId, "", "", "")).IsValid);
    }

    [Fact]
    public async Task FormTemplateOptions_AsInspector_ForwardsNormalizedPagingSearchAndCancellation()
    {
        var repository = ReadyRepository();
        var readService = new FakeReadService(repository)
        {
            Options = new([new(TemplateId, "FORM", "Form", 2)], 2, 5, 1, 1)
        };
        var handler = new GetFormTemplateOptionsQueryHandler(Inspector(), readService);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetFormTemplateOptionsQuery(2, 5, "  safety  "), cancellation.Token);

        Assert.Single(result.Items);
        Assert.Equal(new FormTemplateOptionCriteria(2, 5, "safety"), readService.OptionCriteria);
        Assert.Equal(cancellation.Token, readService.OptionCancellationToken);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator)]
    [InlineData(ApplicationRoles.Manager)]
    [InlineData(ApplicationRoles.Reviewer)]
    public async Task FormTemplateOptions_NonInspector_IsForbiddenBeforeRead(string role)
    {
        var readService = new FakeReadService(ReadyRepository());
        var handler = new GetFormTemplateOptionsQueryHandler(
            new FakeUser(Actor, [role]), readService);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new(), CancellationToken.None));

        Assert.Null(readService.OptionCriteria);
    }

    [Fact]
    public void FormTemplateOptions_ValidatesPagingAndSearch()
    {
        var validator = new GetFormTemplateOptionsQueryValidator();

        Assert.False(validator.Validate(new GetFormTemplateOptionsQuery(0, 20, null)).IsValid);
        Assert.False(validator.Validate(new GetFormTemplateOptionsQuery(1, 101, null)).IsValid);
        Assert.False(validator.Validate(new GetFormTemplateOptionsQuery(1, 20, new string('x', 251))).IsValid);
        Assert.True(validator.Validate(new GetFormTemplateOptionsQuery(2, 100, "safety")).IsValid);
    }

    private static CreateInspectionCommandHandler CreateHandler(
        FakeRepository repository, ICurrentUser? user = null, FakeUnitOfWork? unit = null) => new(
        user ?? Inspector(), new FakeClock(), repository, new FakeReadService(repository),
        new FakeSequence(), Validator(), unit ?? new FakeUnitOfWork());

    private static FormIoDataValidator Validator() => new(new FormIoSchemaValidator());
    private static FakeUser Inspector() => new(Actor, [ApplicationRoles.Inspector]);
    private static FakeRepository ReadyRepository() => new()
    {
        Object = new(ObjectId, true),
        Template = new(TemplateId, true, VersionId, 2, Schema),
        Pinned = new(VersionId, Schema)
    };
    private static FakeReviewerService EligibleReviewers() => new()
    {
        Reviewer = new(ReviewerId, "Reviewer", true, true)
    };

    private static void SeedAggregate(FakeRepository repository)
    {
        var inspection = Inspection.Create(Guid.NewGuid(), "INS-2026-000001", ObjectId, VersionId, "Title", null, Actor, Now);
        var submission = InspectionSubmission.CreateInitial(Guid.NewGuid(), inspection.Id, "{}", Actor, Now);
        inspection.SetCurrentSubmission(submission.Id);
        var process = BusinessProcess.CreateDraft(Guid.NewGuid(), inspection.Id, Actor, Now);
        repository.Inspection = inspection;
        repository.Submission = submission;
        repository.Process = process;
        repository.Submissions.Add(submission);
    }

    private static void SeedSubmitted(FakeRepository repository)
    {
        SeedAggregate(repository);
        repository.Inspection!.AssignReviewer(ReviewerId, Now);
        repository.Process!.Submit(ReviewerId, Now);
        repository.Submission!.Submit(Now);
    }

    private sealed class FakeUser(Guid? id, IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public Guid? UserId => id;
        public IReadOnlyCollection<string> Roles => roles;
        public bool IsInRole(string role) => roles.Contains(role);
    }

    private sealed class FakeClock : IClock { public DateTime UtcNow => Now; }
    private sealed class FakeSequence : IInspectionNumberSequence
    {
        public Task<long> GetNextAsync(CancellationToken cancellationToken) => Task.FromResult(42L);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int TransactionCount { get; private set; }
        public int SaveCount { get; private set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken) { SaveCount++; return Task.CompletedTask; }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        { TransactionCount++; return await operation(cancellationToken); }
    }

    private sealed class FakeRepository : IInspectionRepository
    {
        public InspectionObjectForCreate? Object { get; set; }
        public InspectionTemplateForCreate? Template { get; set; }
        public PinnedTemplateVersion? Pinned { get; set; }
        public Inspection? Inspection { get; set; }
        public InspectionSubmission? Submission { get; set; }
        public BusinessProcess? Process { get; set; }
        public ProcessHistory? History { get; private set; }
        public List<ProcessHistory> Histories { get; } = [];
        public List<InspectionSubmission> Submissions { get; } = [];
        public Guid? PinnedVersionRequested { get; private set; }
        public Task<InspectionObjectForCreate?> GetObjectForCreateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Object);
        public Task<InspectionTemplateForCreate?> GetTemplateForCreateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Template);
        public Task<InspectionWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Inspection is null || Submission is null || Process is null ? null : new InspectionWriteAggregate(Inspection, Submission, Process));
        public Task<PinnedTemplateVersion?> GetPinnedVersionAsync(Guid id, CancellationToken cancellationToken)
        { PinnedVersionRequested = id; return Task.FromResult(Pinned); }
        public void AddInspection(Inspection inspection) => Inspection = inspection;
        public void AddFoundation(InspectionSubmission submission, BusinessProcess process, ProcessHistory history)
        { Submission = submission; Process = process; History = history; Submissions.Add(submission); Histories.Add(history); }
        public void AddSubmission(InspectionSubmission submission)
        { Submission = submission; Submissions.Add(submission); }
        public void AddHistory(ProcessHistory history)
        { History = history; Histories.Add(history); }
    }

    private sealed class FakeReviewerService : IInspectionReviewerService
    {
        public ReviewerEligibility? Reviewer { get; set; }
        public ReviewerOptionCriteria? Criteria { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Task<ReviewerEligibility?> GetForAssignmentAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Reviewer?.UserId == userId ? Reviewer : null);
        public Task<PagedResult<ReviewerOptionDto>> GetOptionsAsync(
            ReviewerOptionCriteria criteria, CancellationToken cancellationToken)
        {
            Criteria = criteria;
            CancellationToken = cancellationToken;
            IReadOnlyCollection<ReviewerOptionDto> items = Reviewer is null
                ? [] : [new(Reviewer.UserId, Reviewer.DisplayName)];
            return Task.FromResult(new PagedResult<ReviewerOptionDto>(
                items, criteria.PageNumber, criteria.PageSize, items.Count, items.Count));
        }
    }

    private sealed class FakeReadService(FakeRepository repository) : IInspectionReadService
    {
        public PagedResult<FormTemplateOptionDto> Options { get; set; } = new([], 1, 20, 0, 0);
        public FormTemplateOptionCriteria? OptionCriteria { get; private set; }
        public CancellationToken OptionCancellationToken { get; private set; }
        public InspectionHistoryCriteria? HistoryCriteria { get; private set; }
        public InspectionRevisionCriteria? RevisionCriteria { get; private set; }
        public CancellationToken WorkflowQueryCancellationToken { get; private set; }
        public Task<PagedResult<InspectionListItemDto>> GetPageAsync(InspectionQueryCriteria criteria, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InspectionDetailDto?> GetDetailAsync(Guid id, InspectionReadScope scope, CancellationToken cancellationToken)
        {
            var i = repository.Inspection!; var s = repository.Submission!; var p = repository.Process!;
            InspectionDetailDto value = new(i.Id, i.Number, i.ConstructionObjectId, "OBJ", "Object", i.Title, i.Description,
                new(TemplateId, "FORM", "Form", VersionId, 2, Schema), p.Status.ToInspectionStatus(), i.CreatedByUserId, i.ReviewerUserId,
                i.CreatedAt, i.UpdatedAt, new(s.Id, 1, s.DataJson, s.CreatedAt, s.UpdatedAt, null, ""), "", "");
            return Task.FromResult<InspectionDetailDto?>(value);
        }
        public Task<PagedResult<FormTemplateOptionDto>> GetFormTemplateOptionsAsync(
            FormTemplateOptionCriteria criteria, CancellationToken cancellationToken)
        {
            OptionCriteria = criteria;
            OptionCancellationToken = cancellationToken;
            return Task.FromResult(Options);
        }
        public Task<PagedResult<ProcessHistoryDto>?> GetHistoryAsync(
            InspectionHistoryCriteria criteria, CancellationToken cancellationToken)
        {
            HistoryCriteria = criteria;
            WorkflowQueryCancellationToken = cancellationToken;
            return Task.FromResult<PagedResult<ProcessHistoryDto>?>(new([], criteria.PageNumber, criteria.PageSize, 0, 0));
        }
        public Task<PagedResult<InspectionRevisionListItemDto>?> GetRevisionsAsync(
            InspectionRevisionCriteria criteria, CancellationToken cancellationToken)
        {
            RevisionCriteria = criteria;
            WorkflowQueryCancellationToken = cancellationToken;
            return Task.FromResult<PagedResult<InspectionRevisionListItemDto>?>(new([], criteria.PageNumber, criteria.PageSize, 0, 0));
        }
        public Task<InspectionRevisionDetailDto?> GetRevisionDetailAsync(
            Guid inspectionId, int revisionNumber, InspectionReadScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<InspectionRevisionDetailDto?>(null);
    }
}
