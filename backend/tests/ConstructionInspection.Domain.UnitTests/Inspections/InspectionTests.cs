using System.Reflection;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Domain.UnitTests.Inspections;

public sealed class InspectionTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime Now = new(2026, 10, 17, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void Create_InitializesPinnedVersionWithoutReviewer()
    {
        var versionId = Guid.NewGuid();
        var inspection = Create(versionId);

        Assert.Equal(versionId, inspection.FormTemplateVersionId);
        Assert.Null(inspection.ReviewerUserId);
        Assert.Equal(Actor, inspection.CreatedByUserId);
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
