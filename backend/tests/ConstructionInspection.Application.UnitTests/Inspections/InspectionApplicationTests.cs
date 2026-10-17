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
        Assert.Equal(1, repository.Submission!.RevisionNumber);
        Assert.Equal(InspectionStatus.Draft, repository.Process!.Status);
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
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(new(
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

    private static void SeedAggregate(FakeRepository repository)
    {
        var inspection = Inspection.Create(Guid.NewGuid(), "INS-2026-000001", ObjectId, VersionId, "Title", null, Actor, Now);
        var submission = InspectionSubmission.CreateInitial(Guid.NewGuid(), inspection.Id, "{}", Actor, Now);
        inspection.SetCurrentSubmission(submission.Id);
        var process = BusinessProcess.CreateDraft(Guid.NewGuid(), inspection.Id, Actor, Now);
        repository.Inspection = inspection;
        repository.Submission = submission;
        repository.Process = process;
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
        public Guid? PinnedVersionRequested { get; private set; }
        public Task<InspectionObjectForCreate?> GetObjectForCreateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Object);
        public Task<InspectionTemplateForCreate?> GetTemplateForCreateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Template);
        public Task<InspectionWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Inspection is null || Submission is null || Process is null ? null : new InspectionWriteAggregate(Inspection, Submission, Process));
        public Task<PinnedTemplateVersion?> GetPinnedVersionAsync(Guid id, CancellationToken cancellationToken)
        { PinnedVersionRequested = id; return Task.FromResult(Pinned); }
        public void AddInspection(Inspection inspection) => Inspection = inspection;
        public void AddFoundation(InspectionSubmission submission, BusinessProcess process, ProcessHistory history)
        { Submission = submission; Process = process; History = history; }
    }

    private sealed class FakeReadService(FakeRepository repository) : IInspectionReadService
    {
        public PagedResult<FormTemplateOptionDto> Options { get; set; } = new([], 1, 20, 0, 0);
        public FormTemplateOptionCriteria? OptionCriteria { get; private set; }
        public CancellationToken OptionCancellationToken { get; private set; }
        public Task<PagedResult<InspectionListItemDto>> GetPageAsync(InspectionQueryCriteria criteria, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InspectionDetailDto?> GetDetailAsync(Guid id, InspectionReadScope scope, CancellationToken cancellationToken)
        {
            var i = repository.Inspection!; var s = repository.Submission!; var p = repository.Process!;
            InspectionDetailDto value = new(i.Id, i.Number, i.ConstructionObjectId, "OBJ", "Object", i.Title, i.Description,
                new(TemplateId, "FORM", "Form", VersionId, 2, Schema), p.Status, i.CreatedByUserId, i.ReviewerUserId,
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
    }
}
