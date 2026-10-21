using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Domain.UnitTests.Checklists;

public sealed class ChecklistTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Reviewer = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly DateTime Now = new(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void Create_InitializesItemsAndCounter()
    {
        var checklist = Create(2);
        Assert.Equal(1, checklist.NextSubmissionNumber);
        Assert.Null(checklist.CurrentSubmissionId);
        Assert.Equal([1, 2], checklist.Items.Select(x => x.Sequence));
        Assert.All(checklist.Items, x => Assert.Equal(ChecklistItemResult.NotChecked, x.Result));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Create_AcceptsItemBoundaries(int count) => Assert.Equal(count, Create(count).Items.Count);

    [Fact]
    public void Create_RejectsOver200Items() => Assert.Equal("checklist.item_count_invalid",
        Assert.Throws<ChecklistDomainException>(() => Create(201)).Code);

    [Fact]
    public void Item_RequiresNoteForFailAndNotApplicable()
    {
        var item = Create(1).Items.Single();
        Assert.Equal("checklist.item_note_required", Assert.Throws<ChecklistDomainException>(() =>
            item.UpdateResult(ChecklistItemResult.Fail, " ", Actor, Now)).Code);
        Assert.Equal("checklist.item_note_required", Assert.Throws<ChecklistDomainException>(() =>
            item.UpdateResult(ChecklistItemResult.NotApplicable, null, Actor, Now)).Code);
        item.UpdateResult(ChecklistItemResult.Pass, null, Actor, Now);
    }

    [Fact]
    public void ChecklistProcess_FollowsLifecycleAndRequiresResume()
    {
        var process = BusinessProcess.CreateForChecklist(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        Assert.Equal(ProcessStatus.Draft, process.StartChecklist(Now));
        process.SubmitChecklist(Reviewer, Now);
        process.StartChecklistReview(Reviewer, Now);
        process.RequestChecklistRecheck(Reviewer, "Fix", Now);
        Assert.Equal("inspection.invalid_state", Assert.Throws<InspectionDomainException>(() =>
            process.SubmitChecklist(Reviewer, Now)).Code);
        Assert.Equal(ProcessStatus.Recheck, process.ResumeChecklist(Now));
        Assert.Equal(ProcessStatus.InProgress, process.Status);
    }

    [Fact]
    public void ApprovedAndRejectedAreTerminal()
    {
        var approved = UnderReview();
        approved.ApproveChecklist(Reviewer, Now);
        Assert.Throws<InspectionDomainException>(() => approved.RequestChecklistRecheck(Reviewer, "No", Now));
        var rejected = UnderReview();
        rejected.RejectChecklist(Reviewer, "No", Now);
        Assert.Throws<InspectionDomainException>(() => rejected.StartChecklist(Now));
    }

    [Fact]
    public void SubmissionCounterAllocatesAndSnapshotStaysImmutable()
    {
        var checklist = Create(1);
        var item = checklist.Items.Single();
        item.UpdateResult(ChecklistItemResult.Pass, null, Actor, Now);
        var submissionId = Guid.NewGuid();
        Assert.Equal(1, checklist.AllocateSubmission(submissionId, Reviewer, Actor, Now));
        var snapshot = ChecklistSubmissionItem.Capture(Guid.NewGuid(), submissionId, item);
        item.UpdateResult(ChecklistItemResult.Fail, "Later", Actor, Now);
        Assert.Equal(ChecklistItemResult.Pass, snapshot.Result);
        Assert.Equal(2, checklist.NextSubmissionNumber);
    }

    [Fact]
    public void ProcessOwnerAndTransitionTypesAreIsolated()
    {
        var inspection = BusinessProcess.CreateForInspection(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        var checklist = BusinessProcess.CreateForChecklist(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        inspection.EnsureOwnerInvariant();
        checklist.EnsureOwnerInvariant();
        Assert.Null(inspection.ChecklistId);
        Assert.Null(checklist.InspectionId);
        Assert.Equal("process.entity_type_mismatch", Assert.Throws<InspectionDomainException>(() =>
            inspection.StartChecklist(Now)).Code);
        Assert.Equal("process.entity_type_mismatch", Assert.Throws<InspectionDomainException>(() =>
            checklist.Submit(Reviewer, Now)).Code);
    }

    [Fact]
    public void NumberUsesUtcYearAndSequence() =>
        Assert.Equal("CHK-2026-000001", ChecklistRules.FormatNumber(Now, 1));

    [Theory]
    [InlineData(InspectionStatus.Draft, ProcessStatus.Draft)]
    [InlineData(InspectionStatus.Submitted, ProcessStatus.Submitted)]
    [InlineData(InspectionStatus.UnderReview, ProcessStatus.UnderReview)]
    [InlineData(InspectionStatus.Approved, ProcessStatus.Approved)]
    [InlineData(InspectionStatus.Recheck, ProcessStatus.Recheck)]
    [InlineData(InspectionStatus.Rejected, ProcessStatus.Rejected)]
    public void InspectionStatusMapping_IsExplicitAndRoundTrips(InspectionStatus inspection, ProcessStatus process)
    {
        Assert.Equal(process, inspection.ToProcessStatus());
        Assert.Equal(inspection, process.ToInspectionStatus());
    }

    [Theory]
    [InlineData(ChecklistStatus.Draft, ProcessStatus.Draft)]
    [InlineData(ChecklistStatus.InProgress, ProcessStatus.InProgress)]
    [InlineData(ChecklistStatus.Submitted, ProcessStatus.Submitted)]
    [InlineData(ChecklistStatus.UnderReview, ProcessStatus.UnderReview)]
    [InlineData(ChecklistStatus.Approved, ProcessStatus.Approved)]
    [InlineData(ChecklistStatus.Recheck, ProcessStatus.Recheck)]
    [InlineData(ChecklistStatus.Rejected, ProcessStatus.Rejected)]
    public void ChecklistStatusMapping_IsExplicitAndRoundTrips(ChecklistStatus checklist, ProcessStatus process)
    {
        Assert.Equal(process, checklist.ToProcessStatus());
        Assert.Equal(checklist, process.ToChecklistStatus());
    }

    [Fact]
    public void InspectionStatusMapping_RejectsChecklistOnlyInProgress() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessStatus.InProgress.ToInspectionStatus());

    private static Checklist Create(int count) => Checklist.Create(Guid.NewGuid(), "CHK-2026-000001",
        Guid.NewGuid(), "Checklist", null, Actor, Now,
        Enumerable.Range(1, count).Select(x => ($"Criterion {x}", true)).ToArray());

    private static BusinessProcess UnderReview()
    {
        var process = BusinessProcess.CreateForChecklist(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        process.StartChecklist(Now);
        process.SubmitChecklist(Reviewer, Now);
        process.StartChecklistReview(Reviewer, Now);
        return process;
    }
}
