using System.Reflection;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Domain.UnitTests.Inspections;

public sealed class InspectionTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Reviewer = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly DateTime Now = new(2026, 10, 17, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void Create_InitializesPinnedVersionWithoutReviewer()
    {
        var versionId = Guid.NewGuid();
        var inspection = Create(versionId);

        Assert.Equal(versionId, inspection.FormTemplateVersionId);
        Assert.Null(inspection.ReviewerUserId);
        Assert.Equal(Actor, inspection.CreatedByUserId);
        Assert.Equal(2, inspection.NextRevisionNumber);
        Assert.Equal(Now, inspection.CreatedAt);
    }

    [Fact]
    public void Create_RejectsRequiredEmptyIdentifiers()
    {
        var error = Assert.Throws<InspectionDomainException>(() => Inspection.Create(
            Guid.NewGuid(), "INS-2026-000001", Guid.Empty, Guid.NewGuid(), "Title", null, Actor, Now));
        Assert.Equal("inspection.invalid_identity", error.Code);
    }

    [Fact]
    public void InitialRevision_IsOneAndDraftUpdateMutatesSameRevision()
    {
        var inspection = Create(Guid.NewGuid());
        var submission = InspectionSubmission.CreateInitial(Guid.NewGuid(), inspection.Id, "{}", Actor, Now);
        inspection.SetCurrentSubmission(submission.Id);

        submission.UpdateDraft("{\"ok\":true}", Actor, Now.AddMinutes(1));

        Assert.Equal(1, submission.RevisionNumber);
        Assert.Equal("{\"ok\":true}", submission.DataJson);
        Assert.Equal(submission.Id, inspection.CurrentSubmissionId);
    }

    [Fact]
    public void ProcessAndInitialHistory_UseDraftAndExactAudit()
    {
        var inspection = Create(Guid.NewGuid());
        var submission = InspectionSubmission.CreateInitial(Guid.NewGuid(), inspection.Id, "{}", Actor, Now);
        var process = BusinessProcess.CreateDraft(Guid.NewGuid(), inspection.Id, Actor, Now);
        var history = ProcessHistory.CreateInitial(Guid.NewGuid(), process.Id, submission.Id, Actor, Now);

        Assert.Equal(InspectionStatus.Draft, process.Status);
        Assert.Null(process.AssignedToUserId);
        Assert.Null(history.FromStatus);
        Assert.Equal(InspectionStatus.Draft, history.ToStatus);
        Assert.Equal("Create", history.Action);
        Assert.Equal(Actor, history.PerformedByUserId);
        Assert.Equal(Now, history.PerformedAt);
        Assert.Equal(submission.Id, history.SubmissionId);
    }

    [Fact]
    public void ProcessHistory_HasNoPublicMutationMethod()
    {
        var methods = typeof(ProcessHistory).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(methods, method => !method.IsSpecialName);
    }

    [Fact]
    public void EnsureDraft_RejectsNonDraftPersistedState()
    {
        var process = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        typeof(BusinessProcess).GetProperty(nameof(BusinessProcess.Status))!
            .SetValue(process, InspectionStatus.Submitted);

        var error = Assert.Throws<InspectionDomainException>(process.EnsureDraft);
        Assert.Equal("inspection.invalid_state", error.Code);
    }

    [Fact]
    public void Workflow_ValidTransitionsReachApprovedTerminal()
    {
        var process = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);

        Assert.Equal(InspectionStatus.Draft, process.Submit(Reviewer, Now));
        Assert.Equal(InspectionStatus.Submitted, process.StartReview(Reviewer, Now.AddMinutes(1)));
        Assert.Equal(InspectionStatus.UnderReview, process.Approve(Reviewer, Now.AddMinutes(2)));
        Assert.Equal(InspectionStatus.Approved, process.Status);
        Assert.NotNull(process.CompletedAt);

        var error = Assert.Throws<InspectionDomainException>(() =>
            process.StartReview(Reviewer, Now.AddMinutes(3)));
        Assert.Equal("inspection.invalid_state", error.Code);
    }

    [Fact]
    public void Workflow_RejectAndRecheckRequireReasonsAndRejectedIsTerminal()
    {
        var rejected = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        rejected.Submit(Reviewer, Now);
        rejected.StartReview(Reviewer, Now);
        Assert.Equal("inspection.reason_required", Assert.Throws<InspectionDomainException>(() =>
            rejected.Reject(Reviewer, " ", Now)).Code);
        rejected.Reject(Reviewer, "Not compliant", Now);
        Assert.Equal(InspectionStatus.Rejected, rejected.Status);
        Assert.Equal("inspection.invalid_state", Assert.Throws<InspectionDomainException>(() =>
            rejected.RequestRecheck(Reviewer, "Try again", Now)).Code);

        var recheck = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        recheck.Submit(Reviewer, Now);
        recheck.StartReview(Reviewer, Now);
        recheck.RequestRecheck(Reviewer, "Correct values", Now);
        Assert.Equal(InspectionStatus.Recheck, recheck.Status);
        Assert.Equal(InspectionStatus.Recheck, recheck.Submit(Reviewer, Now));
    }

    [Fact]
    public void Reassign_SubmittedStaysSubmitted_UnderReviewReturnsSubmitted()
    {
        var replacement = Guid.NewGuid();
        var submitted = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        submitted.Submit(Reviewer, Now);
        var first = submitted.ReassignReviewer(replacement, "Coverage", Now);
        Assert.Equal(InspectionStatus.Submitted, first.FromStatus);
        Assert.Equal(InspectionStatus.Submitted, submitted.Status);

        var underReview = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        underReview.Submit(Reviewer, Now);
        underReview.StartReview(Reviewer, Now);
        var second = underReview.ReassignReviewer(replacement, "Coverage", Now);
        Assert.Equal(InspectionStatus.UnderReview, second.FromStatus);
        Assert.Equal(InspectionStatus.Submitted, underReview.Status);
        Assert.Equal(replacement, underReview.AssignedToUserId);
    }

    [Fact]
    public void Workflow_InvalidEdgesAndReassignStatesAreRejected()
    {
        var draft = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        Assert.Equal("inspection.not_assigned_reviewer", Assert.Throws<InspectionDomainException>(() =>
            draft.StartReview(Reviewer, Now)).Code);
        Assert.Equal("inspection.invalid_state", Assert.Throws<InspectionDomainException>(() =>
            draft.ReassignReviewer(Guid.NewGuid(), "Coverage", Now)).Code);

        var submitted = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        submitted.Submit(Reviewer, Now);
        Assert.Equal("inspection.invalid_state", Assert.Throws<InspectionDomainException>(() =>
            submitted.Approve(Reviewer, Now)).Code);
        Assert.Equal("inspection.not_assigned_reviewer", Assert.Throws<InspectionDomainException>(() =>
            submitted.StartReview(Guid.NewGuid(), Now)).Code);

        var recheck = BusinessProcess.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        recheck.Submit(Reviewer, Now);
        recheck.StartReview(Reviewer, Now);
        recheck.RequestRecheck(Reviewer, "Correct", Now);
        Assert.Equal("inspection.invalid_state", Assert.Throws<InspectionDomainException>(() =>
            recheck.ReassignReviewer(Guid.NewGuid(), "Coverage", Now)).Code);
    }

    [Fact]
    public void AllocateRevision_UsesCounterAndSubmittedRevisionIsImmutable()
    {
        var inspection = Create(Guid.NewGuid());
        var initial = InspectionSubmission.CreateInitial(Guid.NewGuid(), inspection.Id, "{}", Actor, Now);
        initial.Submit(Now);

        var newId = Guid.NewGuid();
        var number = inspection.AllocateRevision(newId, Actor, Now.AddMinutes(1));
        var revision = InspectionSubmission.CreateRevision(
            newId, inspection.Id, number, initial.DataJson, Actor, Now.AddMinutes(1));

        Assert.Equal(2, number);
        Assert.Equal(3, inspection.NextRevisionNumber);
        Assert.Equal(newId, inspection.CurrentSubmissionId);
        Assert.Equal("inspection.revision_immutable", Assert.Throws<InspectionDomainException>(() =>
            initial.UpdateDraft("{\"changed\":true}", Actor, Now)).Code);
        Assert.Equal(2, revision.RevisionNumber);
    }

    [Theory]
    [InlineData(2026, 1, "INS-2026-000001")]
    [InlineData(2027, 1000000, "INS-2027-1000000")]
    public void FormatNumber_UsesUtcYearAndInvariantSequence(int year, long sequence, string expected)
    {
        var utc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, InspectionRules.FormatNumber(utc, sequence));
    }

    private static Inspection Create(Guid versionId) => Inspection.Create(
        Guid.NewGuid(), "INS-2026-000001", Guid.NewGuid(), versionId, " Inspection ", null, Actor, Now);
}
