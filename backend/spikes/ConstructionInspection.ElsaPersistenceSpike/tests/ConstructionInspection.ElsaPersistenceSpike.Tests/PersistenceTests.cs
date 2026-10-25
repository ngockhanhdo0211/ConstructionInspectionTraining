using Xunit;

namespace ConstructionInspection.ElsaPersistenceSpike.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Approved_flow_survives_restart_as_terminal()
    {
        var inspectionId = Guid.NewGuid();
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            Assert.Equal(DispatchResult.Processed, await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Created)));
            Assert.Equal(DispatchResult.Processed, await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Submitted)));
            Assert.Equal(DispatchResult.Processed, await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.StartReview)));
            Assert.Equal(DispatchResult.Processed, await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Approved)));
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        var snapshot = await restarted.GetSnapshotAsync(inspectionId);
        Assert.Equal(ShadowStates.Approved, snapshot.ShadowState);
        Assert.Empty(snapshot.BookmarkEvents);
    }

    [Fact]
    public async Task Suspended_bookmark_resumes_after_restart()
    {
        var inspectionId = Guid.NewGuid();
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Created));
            await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Submitted));
            Assert.Contains(InspectionEventTypes.StartReview, (await host.GetSnapshotAsync(inspectionId)).BookmarkEvents);
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        Assert.Equal(DispatchResult.Processed, await restarted.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.StartReview)));
        Assert.Equal(ShadowStates.UnderReview, (await restarted.GetSnapshotAsync(inspectionId)).ShadowState);
    }

    [Fact]
    public async Task Recheck_revision_resubmit_restart_and_approve_is_durable()
    {
        var inspectionId = Guid.NewGuid();
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            foreach (var type in new[]
                     {
                         InspectionEventTypes.Created,
                         InspectionEventTypes.Submitted,
                         InspectionEventTypes.StartReview,
                         InspectionEventTypes.RecheckRequested,
                         InspectionEventTypes.RevisionCreated,
                         InspectionEventTypes.Resubmitted
                     })
                Assert.Equal(DispatchResult.Processed, await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, type, revision: 2)));
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        await restarted.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.StartReview, revision: 2));
        await restarted.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Approved, revision: 2));
        Assert.Equal(ShadowStates.Approved, (await restarted.GetSnapshotAsync(inspectionId)).ShadowState);
    }

    [Fact]
    public async Task Reassign_returns_to_submitted_and_start_review_is_available()
    {
        var inspectionId = Guid.NewGuid();
        await using var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        foreach (var type in new[]
                 {
                     InspectionEventTypes.Created,
                     InspectionEventTypes.Submitted,
                     InspectionEventTypes.StartReview,
                     InspectionEventTypes.ReviewerReassigned
                 })
            await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, type));

        var snapshot = await host.GetSnapshotAsync(inspectionId);
        Assert.Equal(ShadowStates.Submitted, snapshot.ShadowState);
        Assert.Contains(InspectionEventTypes.StartReview, snapshot.BookmarkEvents);
    }

    [Fact]
    public async Task Sequential_concurrent_restart_and_terminal_duplicates_are_no_ops()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Created);
        await using (var first = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            Assert.Equal(DispatchResult.Processed, await first.DispatchAsync(created));
            Assert.Equal(DispatchResult.Duplicate, await first.DispatchAsync(created));
        }

        await using var hostA = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        await using var hostB = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        Assert.Equal(DispatchResult.Duplicate, await hostA.DispatchAsync(created));
        var submitted = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Submitted);
        var concurrent = await Task.WhenAll(hostA.DispatchAsync(submitted), hostB.DispatchAsync(submitted));
        Assert.Contains(DispatchResult.Processed, concurrent);
        Assert.Contains(DispatchResult.Duplicate, concurrent);
        Assert.Equal(1, await hostA.CountInboxEffectsAsync(submitted.DomainEventId));

        await hostA.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.StartReview));
        var approved = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Approved);
        Assert.Equal(DispatchResult.Processed, await hostA.DispatchAsync(approved));
        Assert.Equal(DispatchResult.Duplicate, await hostB.DispatchAsync(approved));
    }

    [Fact]
    public async Task Payload_conflict_unknown_and_out_of_order_fail_closed()
    {
        var inspectionId = Guid.NewGuid();
        await using var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        var created = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Created);
        await host.DispatchAsync(created);
        var changed = created with { ActorUserId = Guid.NewGuid() };
        Assert.Equal(DispatchResult.PayloadConflict, await host.DispatchAsync(changed));
        Assert.Equal(DispatchResult.OutOfOrder, await host.DispatchAsync(SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Approved)));
        await Assert.ThrowsAsync<UnknownEventTypeException>(() =>
            host.DispatchAsync(SpikeTestSupport.Event(Guid.NewGuid(), "Inspection.Unknown")));
    }

    [Fact]
    public async Task Crash_windows_are_visible_and_reconcilable_without_double_transition()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Created);
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            await Assert.ThrowsAsync<SimulatedCrashException>(() => host.DispatchAsync(created, CrashPoint.BeforeInboxCommit));
            Assert.Equal(DispatchResult.Processed, await host.DispatchAsync(created));

            var submitted = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.Submitted);
            await Assert.ThrowsAsync<SimulatedCrashException>(() => host.DispatchAsync(submitted, CrashPoint.AfterInboxCommitBeforeElsa));
            Assert.Equal(DispatchResult.Processed, await host.ReconcileAsync(submitted.DomainEventId));

            var startReview = SpikeTestSupport.Event(inspectionId, InspectionEventTypes.StartReview);
            await Assert.ThrowsAsync<SimulatedCrashException>(() => host.DispatchAsync(startReview, CrashPoint.AfterElsaPersistenceBeforeInboxProcessed));
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        var pendingId = (await restarted.GetSnapshotAsync(inspectionId)).LastDomainEventId!.Value;
        Assert.Equal(DispatchResult.Reconciled, await restarted.ReconcileAsync(pendingId));
        Assert.Equal(ShadowStates.UnderReview, (await restarted.GetSnapshotAsync(inspectionId)).ShadowState);
        Assert.Equal(1, await restarted.CountInboxEffectsAsync(pendingId));
    }

    [Fact]
    public async Task Cancellation_is_observed_and_host_leaves_no_listener()
    {
        await using var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.DispatchAsync(SpikeTestSupport.Event(Guid.NewGuid(), InspectionEventTypes.Created), cancellationToken: cancellation.Token));
    }
}
