using System.Text.Json;
using Xunit;

namespace ConstructionInspection.ElsaSpike.Tests;

public sealed class InspectionLifecycleWorkflowTests(SpikeFixture fixture) : IClassFixture<SpikeFixture>
{
    [Fact]
    public async Task WorkflowStartSucceeds()
    {
        var inspectionId = Guid.NewGuid();
        var instanceId = await fixture.Host.StartAsync(inspectionId, Guid.NewGuid(), Guid.NewGuid());
        var state = await fixture.Host.GetStateAsync(inspectionId);

        Assert.False(string.IsNullOrWhiteSpace(instanceId));
        Assert.Equal(InspectionSpikeHost.CanonicalCorrelation(inspectionId), state.CorrelationId);
    }

    [Fact]
    public async Task InstanceSuspendsAtSubmitted()
    {
        var inspectionId = await StartAsync();
        var state = await fixture.Host.GetStateAsync(inspectionId);

        Assert.Equal("Suspended", state.SubStatus.ToString());
        AssertBookmark(state, InspectionEventTypes.Submitted, inspectionId);
    }

    [Fact]
    public async Task WrongInspectionIdDoesNotResume()
    {
        var inspectionId = await StartAsync();
        var result = await fixture.Host.SendAsync(Event(Guid.NewGuid(), InspectionEventTypes.Submitted));

        Assert.Equal(DispatchResult.InstanceNotFound, result);
        AssertBookmark(await fixture.Host.GetStateAsync(inspectionId), InspectionEventTypes.Submitted, inspectionId);
    }

    [Fact]
    public async Task SubmittedResumesCorrectInstance()
    {
        var inspectionId = await StartAsync();

        var result = await fixture.Host.SendAsync(Event(inspectionId, InspectionEventTypes.Submitted));

        Assert.Equal(DispatchResult.Processed, result);
        AssertBookmark(await fixture.Host.GetStateAsync(inspectionId), InspectionEventTypes.StartReview, inspectionId);
    }

    [Fact]
    public async Task InstanceSuspendsAtStartReview()
    {
        var inspectionId = await StartAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Submitted);
        var state = await fixture.Host.GetStateAsync(inspectionId);

        Assert.Equal("Suspended", state.SubStatus.ToString());
        Assert.Single(state.Bookmarks);
        AssertBookmark(state, InspectionEventTypes.StartReview, inspectionId);
    }

    [Fact]
    public async Task StartReviewResumesIntoDecisionWait()
    {
        var inspectionId = await StartThroughReviewAsync();
        var state = await fixture.Host.GetStateAsync(inspectionId);

        Assert.Equal(4, state.Bookmarks.Count);
        AssertBookmark(state, InspectionEventTypes.Approved, inspectionId);
        AssertBookmark(state, InspectionEventTypes.Rejected, inspectionId);
        AssertBookmark(state, InspectionEventTypes.Recheck, inspectionId);
        AssertBookmark(state, InspectionEventTypes.Reassigned, inspectionId);
    }

    [Fact]
    public async Task ApproveFinishesWorkflow()
    {
        var inspectionId = await StartThroughReviewAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Approved);

        Assert.Equal("Finished", (await fixture.Host.GetStateAsync(inspectionId)).SubStatus.ToString());
    }

    [Fact]
    public async Task RejectFinishesWorkflow()
    {
        var inspectionId = await StartThroughReviewAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Rejected);

        Assert.Equal("Finished", (await fixture.Host.GetStateAsync(inspectionId)).SubStatus.ToString());
    }

    [Fact]
    public async Task RecheckCreatesRevisionLoop()
    {
        var inspectionId = await StartThroughReviewAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Recheck);

        AssertBookmark(await fixture.Host.GetStateAsync(inspectionId), InspectionEventTypes.RevisionCreated, inspectionId);
    }

    [Fact]
    public async Task RevisionCreatedAndResubmittedReturnToStartReview()
    {
        var inspectionId = await StartThroughReviewAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Recheck);
        await SendAsync(inspectionId, InspectionEventTypes.RevisionCreated, 2);
        await SendAsync(inspectionId, InspectionEventTypes.Resubmitted, 2);

        AssertBookmark(await fixture.Host.GetStateAsync(inspectionId), InspectionEventTypes.StartReview, inspectionId);
    }

    [Fact]
    public async Task ReassignFromUnderReviewReturnsToStartReview()
    {
        var inspectionId = await StartThroughReviewAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Reassigned);
        var state = await fixture.Host.GetStateAsync(inspectionId);

        Assert.Single(state.Bookmarks);
        AssertBookmark(state, InspectionEventTypes.StartReview, inspectionId);
    }

    [Fact]
    public async Task ParallelInspectionsDoNotCrossResume()
    {
        var first = await StartAsync();
        var second = await StartAsync();

        await SendAsync(first, InspectionEventTypes.Submitted);

        AssertBookmark(await fixture.Host.GetStateAsync(first), InspectionEventTypes.StartReview, first);
        AssertBookmark(await fixture.Host.GetStateAsync(second), InspectionEventTypes.Submitted, second);
    }

    [Fact]
    public async Task ConcurrentDuplicateDomainEventExecutesOnce()
    {
        var inspectionId = await StartAsync();
        var payload = Event(inspectionId, InspectionEventTypes.Submitted);

        var results = await Task.WhenAll(
            fixture.Host.SendAsync(payload),
            fixture.Host.SendAsync(payload));

        Assert.Contains(DispatchResult.Processed, results);
        Assert.Contains(DispatchResult.Duplicate, results);
        Assert.Single(fixture.Host.Probe.Events, item => item.DomainEventId == payload.DomainEventId);
    }

    [Fact]
    public async Task ConcurrentDuplicateDecisionCompletesOnce()
    {
        var inspectionId = await StartThroughReviewAsync();
        var first = Event(inspectionId, InspectionEventTypes.Approved);
        var second = Event(inspectionId, InspectionEventTypes.Approved);

        var results = await Task.WhenAll(
            fixture.Host.SendAsync(first),
            fixture.Host.SendAsync(second));
        var state = await fixture.Host.GetStateAsync(inspectionId);

        Assert.Contains(DispatchResult.Processed, results);
        Assert.Contains(DispatchResult.NoMatchingBookmark, results);
        Assert.Equal("Finished", state.SubStatus.ToString());
        Assert.Single(fixture.Host.Probe.Events, item =>
            item.InspectionId == inspectionId && item.EventType == InspectionEventTypes.Approved);
    }

    [Fact]
    public async Task OutOfOrderEventDoesNotAdvanceWorkflow()
    {
        var inspectionId = await StartAsync();

        var result = await fixture.Host.SendAsync(Event(inspectionId, InspectionEventTypes.StartReview));

        Assert.Equal(DispatchResult.NoMatchingBookmark, result);
        AssertBookmark(await fixture.Host.GetStateAsync(inspectionId), InspectionEventTypes.Submitted, inspectionId);
    }

    [Fact]
    public async Task UnknownEventFailsClosed()
    {
        var inspectionId = await StartAsync();

        var result = await fixture.Host.SendAsync(Event(inspectionId, "Inspection.Unknown"));

        Assert.Equal(DispatchResult.UnknownEvent, result);
        AssertBookmark(await fixture.Host.GetStateAsync(inspectionId), InspectionEventTypes.Submitted, inspectionId);
    }

    [Fact]
    public async Task CancellationTokenIsPropagated()
    {
        var inspectionId = await StartAsync();
        using var liveCancellation = new CancellationTokenSource();
        await fixture.Host.SendAsync(
            Event(inspectionId, InspectionEventTypes.Submitted),
            liveCancellation.Token);

        Assert.True(fixture.Host.Probe.SawCancelableToken);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Host.SendAsync(Event(inspectionId, InspectionEventTypes.StartReview), cancelled.Token));
    }

    [Fact]
    public void WorkflowPayloadContainsNoSensitiveFields()
    {
        var json = JsonSerializer.Serialize(Event(Guid.NewGuid(), InspectionEventTypes.Submitted));

        Assert.DoesNotContain("jwt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("datajson", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionstring", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerminalWorkflowHasNoBusinessBookmarks()
    {
        var inspectionId = await StartThroughReviewAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Approved);

        Assert.Empty((await fixture.Host.GetStateAsync(inspectionId)).Bookmarks);
    }

    [Fact]
    public async Task NewInMemoryHostCannotRecoverPriorInstance()
    {
        var inspectionId = Guid.NewGuid();
        var instanceId = await fixture.Host.StartAsync(inspectionId, Guid.NewGuid(), Guid.NewGuid());
        await using var restartedHost = await InspectionSpikeHost.CreateAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => restartedHost.GetStateByInstanceIdAsync(instanceId));
    }

    private async Task<Guid> StartAsync()
    {
        var inspectionId = Guid.NewGuid();
        await fixture.Host.StartAsync(inspectionId, Guid.NewGuid(), Guid.NewGuid());
        return inspectionId;
    }

    private async Task<Guid> StartThroughReviewAsync()
    {
        var inspectionId = await StartAsync();
        await SendAsync(inspectionId, InspectionEventTypes.Submitted);
        await SendAsync(inspectionId, InspectionEventTypes.StartReview);
        return inspectionId;
    }

    private async Task SendAsync(Guid inspectionId, string eventType, int revision = 1)
    {
        var result = await fixture.Host.SendAsync(Event(inspectionId, eventType, revision));
        Assert.Equal(DispatchResult.Processed, result);
    }

    private static InspectionEventPayload Event(Guid inspectionId, string eventType, int revision = 1) =>
        new(
            Guid.NewGuid(),
            inspectionId,
            Guid.NewGuid(),
            eventType,
            Guid.NewGuid(),
            revision,
            DateTimeOffset.UtcNow);

    private static void AssertBookmark(
        Elsa.Workflows.State.WorkflowState state,
        string eventType,
        Guid inspectionId) =>
        Assert.Contains(
            state.Bookmarks,
            bookmark => Equals(
                bookmark.Payload,
                new InspectionStimulus(eventType, InspectionSpikeHost.CanonicalCorrelation(inspectionId))));
}

public sealed class SpikeFixture : IAsyncLifetime
{
    public InspectionSpikeHost Host { get; private set; } = null!;

    public async Task InitializeAsync() => Host = await InspectionSpikeHost.CreateAsync();

    public async Task DisposeAsync() => await Host.DisposeAsync();
}
