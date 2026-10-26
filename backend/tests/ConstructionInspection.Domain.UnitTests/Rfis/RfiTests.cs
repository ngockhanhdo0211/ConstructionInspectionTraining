using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Rfis;

namespace ConstructionInspection.Domain.UnitTests.Rfis;

public sealed class RfiTests
{
    private static readonly Guid Creator = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Reviewer = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid OtherReviewer = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly DateTime Now = new(2026, 10, 26, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void Create_InitializesImmutableIdentityAndCounter()
    {
        var rfi = Create();
        Assert.Equal("RFI-2026-000001", rfi.Number);
        Assert.Equal(1, rfi.NextEntryNumber);
        Assert.Empty(rfi.Entries);
        Assert.Equal(Creator, rfi.CreatedByUserId);
    }

    [Fact]
    public void SaveDraft_TrimsAndRequiresCreator()
    {
        var rfi = Create();
        rfi.SaveDraft(" New ", " Question 2 ", Creator, Now.AddMinutes(1));
        Assert.Equal("New", rfi.Subject);
        Assert.Equal("Question 2", rfi.Question);
        Assert.Equal("rfi.not_creator", Assert.Throws<RfiDomainException>(() =>
            rfi.SaveDraft("X", "Y", Reviewer, Now)).Code);
    }

    [Fact]
    public void Content_UsesUtf8ByteBoundary()
    {
        var exact = new string('a', RfiRules.MaximumContentUtf8Bytes);
        var rfi = Rfi.Create(Guid.NewGuid(), "RFI-2026-000001", Guid.NewGuid(), "S", exact, Creator, Now);
        Assert.Equal(exact.Length, rfi.Question.Length);
        var over = new string('é', RfiRules.MaximumContentUtf8Bytes / 2 + 1);
        Assert.Equal("rfi.question_invalid", Assert.Throws<RfiDomainException>(() =>
            Rfi.Create(Guid.NewGuid(), "RFI-2026-000002", Guid.NewGuid(), "S", over, Creator, Now)).Code);
    }

    [Fact]
    public void Entries_AreAppendOnlyAndAllocateStableNumbers()
    {
        var rfi = Create();
        var first = rfi.AppendEntry(Guid.NewGuid(), RfiEntryType.Response, "Answer", Reviewer, Now);
        var second = rfi.AppendEntry(Guid.NewGuid(), RfiEntryType.ClarificationRequest, "More", Creator, Now);
        Assert.Equal([1, 2], rfi.Entries.Select(x => x.EntryNumber));
        Assert.Equal(RfiEntryType.Response, first.EntryType);
        Assert.Equal(RfiEntryType.ClarificationRequest, second.EntryType);
        Assert.Equal(3, rfi.NextEntryNumber);
    }

    [Fact]
    public void Process_FollowsFullLifecycleAndResetsDueAt()
    {
        var process = BusinessProcess.CreateForRfi(Guid.NewGuid(), Guid.NewGuid(), Creator, Now);
        Assert.Equal(ProcessStatus.Draft, process.SubmitRfi(Reviewer, Now));
        Assert.Equal(Now.AddHours(72), process.DueAt);
        process.RespondRfi(Reviewer, Now.AddHours(1));
        Assert.Null(process.DueAt);
        process.RequestRfiClarification(Now.AddHours(2));
        Assert.Equal(Now.AddHours(74), process.DueAt);
        process.RespondRfi(Reviewer, Now.AddHours(3));
        process.CloseRfi(Now.AddHours(4));
        Assert.Equal(ProcessStatus.Closed, process.Status);
        Assert.Equal(Now.AddHours(4), process.CompletedAt);
    }

    [Fact]
    public void Reassign_RequiresOpenDifferentReviewerAndResetsSla()
    {
        var process = BusinessProcess.CreateForRfi(Guid.NewGuid(), Guid.NewGuid(), Creator, Now);
        process.SubmitRfi(Reviewer, Now);
        var change = process.ReassignRfi(OtherReviewer, "Capacity", Now.AddHour());
        Assert.Equal(Reviewer, change.PreviousReviewerUserId);
        Assert.Equal(OtherReviewer, process.AssignedToUserId);
        Assert.Equal(Now.AddHours(73), process.DueAt);
        Assert.Equal("rfi.reviewer_unchanged", Assert.Throws<RfiDomainException>(() =>
            process.ReassignRfi(OtherReviewer, "Again", Now)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_DraftOrOpen_IsTerminal(bool submit)
    {
        var process = BusinessProcess.CreateForRfi(Guid.NewGuid(), Guid.NewGuid(), Creator, Now);
        if (submit) process.SubmitRfi(Reviewer, Now);
        process.CancelRfi("No longer required", Now.AddMinutes(1));
        Assert.Equal(ProcessStatus.Cancelled, process.Status);
        Assert.Throws<RfiDomainException>(() => process.SubmitRfi(Reviewer, Now));
    }

    [Fact]
    public void CrossModuleTransitions_FailClosed()
    {
        var inspection = BusinessProcess.CreateForInspection(Guid.NewGuid(), Guid.NewGuid(), Creator, Now);
        Assert.Equal("process.entity_type_mismatch", Assert.Throws<RfiDomainException>(() =>
            inspection.SubmitRfi(Reviewer, Now)).Code);
    }

    [Theory]
    [InlineData(RfiStatus.Draft, ProcessStatus.Draft)]
    [InlineData(RfiStatus.Open, ProcessStatus.Open)]
    [InlineData(RfiStatus.Responded, ProcessStatus.Responded)]
    [InlineData(RfiStatus.Closed, ProcessStatus.Closed)]
    [InlineData(RfiStatus.Cancelled, ProcessStatus.Cancelled)]
    public void StatusMapping_IsExplicit(RfiStatus rfi, ProcessStatus process)
    {
        Assert.Equal(process, rfi.ToProcessStatus());
        Assert.Equal(rfi, process.ToRfiStatus());
    }

    [Fact]
    public void UnsupportedStatusMapping_FailsClosed() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessStatus.Approved.ToRfiStatus());

    private static Rfi Create() => Rfi.Create(Guid.NewGuid(), "RFI-2026-000001",
        Guid.NewGuid(), "Subject", "Question", Creator, Now);
}

internal static class DateTimeTestExtensions
{
    public static DateTime AddHour(this DateTime value) => value.AddHours(1);
}
