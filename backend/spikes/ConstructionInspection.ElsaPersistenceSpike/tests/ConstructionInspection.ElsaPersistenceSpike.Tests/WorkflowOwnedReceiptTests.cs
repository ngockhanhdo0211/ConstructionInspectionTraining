using Xunit;

namespace ConstructionInspection.ElsaPersistenceSpike.Tests;

public sealed class WorkflowOwnedReceiptTests
{
    [Fact]
    public async Task Receipt_sequence_duplicate_conflict_obsolete_and_gap_are_fail_closed()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created);
        await using var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);

        Assert.Equal(WorkflowOwnedDisposition.Applied, await host.DispatchWorkflowOwnedAsync(created));
        Assert.Equal(WorkflowOwnedDisposition.Duplicate, await host.DispatchWorkflowOwnedAsync(created));

        var conflict = SpikeTestSupport.OwnedEvent(
            inspectionId,
            1,
            InspectionEventTypes.Created,
            created.DomainEventId);
        Assert.NotEqual(created.PayloadHash, conflict.PayloadHash);
        Assert.Equal(WorkflowOwnedDisposition.Conflict, await host.DispatchWorkflowOwnedAsync(conflict));
        Assert.Equal(
            WorkflowOwnedDisposition.Gap,
            await host.DispatchWorkflowOwnedAsync(SpikeTestSupport.OwnedEvent(inspectionId, 3, InspectionEventTypes.StartReview)));

        var submitted = SpikeTestSupport.OwnedEvent(inspectionId, 2, InspectionEventTypes.Submitted);
        Assert.Equal(WorkflowOwnedDisposition.Applied, await host.DispatchWorkflowOwnedAsync(submitted));
        Assert.Equal(WorkflowOwnedDisposition.Obsolete, await host.DispatchWorkflowOwnedAsync(created));

        var snapshot = await host.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(2, snapshot.LastAppliedSequence);
        Assert.Equal(submitted.DomainEventId, snapshot.LastDomainEventId);
        Assert.Equal(ShadowStates.Submitted, snapshot.CurrentShadowState);
        Assert.Equal(1, snapshot.BookmarkCount);
    }

    [Fact]
    public async Task Full_recheck_reassign_approve_flow_and_terminal_receipt_survive_restarts()
    {
        var inspectionId = Guid.NewGuid();
        var events = new[]
        {
            SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created),
            SpikeTestSupport.OwnedEvent(inspectionId, 2, InspectionEventTypes.Submitted),
            SpikeTestSupport.OwnedEvent(inspectionId, 3, InspectionEventTypes.ReviewerReassigned),
            SpikeTestSupport.OwnedEvent(inspectionId, 4, InspectionEventTypes.StartReview),
            SpikeTestSupport.OwnedEvent(inspectionId, 5, InspectionEventTypes.RecheckRequested),
            SpikeTestSupport.OwnedEvent(inspectionId, 6, InspectionEventTypes.RevisionCreated),
            SpikeTestSupport.OwnedEvent(inspectionId, 7, InspectionEventTypes.Resubmitted),
            SpikeTestSupport.OwnedEvent(inspectionId, 8, InspectionEventTypes.StartReview),
            SpikeTestSupport.OwnedEvent(inspectionId, 9, InspectionEventTypes.Approved)
        };

        foreach (var item in events)
        {
            await using var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
            Assert.Equal(WorkflowOwnedDisposition.Applied, await host.DispatchWorkflowOwnedAsync(item));
            var snapshot = await host.GetWorkflowOwnedSnapshotAsync(inspectionId);
            Assert.Equal(item.AggregateSequence, snapshot.LastAppliedSequence);
            Assert.Equal(item.DomainEventId, snapshot.LastDomainEventId);
            Assert.Equal(1, snapshot.BookmarkCount);
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        Assert.Equal(WorkflowOwnedDisposition.Duplicate, await restarted.DispatchWorkflowOwnedAsync(events[^1]));
        Assert.Equal(
            WorkflowOwnedDisposition.TerminalConflict,
            await restarted.DispatchWorkflowOwnedAsync(
                SpikeTestSupport.OwnedEvent(inspectionId, 10, InspectionEventTypes.Rejected)));
        var terminal = await restarted.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(ShadowStates.Approved, terminal.CurrentShadowState);
        Assert.Equal(9, terminal.LastAppliedSequence);
    }

    [Fact]
    public async Task Lost_ack_after_commit_is_an_exact_duplicate_after_restart()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created);
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
            Assert.Equal(WorkflowOwnedDisposition.Applied, await host.DispatchWorkflowOwnedAsync(created));

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        Assert.Equal(WorkflowOwnedDisposition.Duplicate, await restarted.DispatchWorkflowOwnedAsync(created));
        Assert.Equal(1, (await restarted.GetWorkflowOwnedSnapshotAsync(inspectionId)).LastAppliedSequence);
    }

    [Fact]
    public async Task Two_hosts_serialize_same_and_competing_sequences_without_double_effect()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created);
        await using var hostA = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        await using var hostB = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        await hostA.DispatchWorkflowOwnedAsync(created);

        var submitted = SpikeTestSupport.OwnedEvent(inspectionId, 2, InspectionEventTypes.Submitted);
        var same = await Task.WhenAll(
            hostA.DispatchWorkflowOwnedAsync(submitted),
            hostB.DispatchWorkflowOwnedAsync(submitted));
        Assert.All(same, x => Assert.Contains(x, new[] { WorkflowOwnedDisposition.Applied, WorkflowOwnedDisposition.Duplicate }));
        var afterSame = await hostA.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(2, afterSame.LastAppliedSequence);
        Assert.Equal(submitted.DomainEventId, afterSame.LastDomainEventId);

        var startA = SpikeTestSupport.OwnedEvent(inspectionId, 3, InspectionEventTypes.StartReview);
        var startB = SpikeTestSupport.OwnedEvent(inspectionId, 3, InspectionEventTypes.StartReview);
        var competing = await Task.WhenAll(
            hostA.DispatchWorkflowOwnedAsync(startA),
            hostB.DispatchWorkflowOwnedAsync(startB));
        var afterCompeting = await hostA.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(3, afterCompeting.LastAppliedSequence);
        Assert.Contains(afterCompeting.LastDomainEventId, new[] { startA.DomainEventId, startB.DomainEventId });
        Assert.Contains(WorkflowOwnedDisposition.Applied, competing);

        var approve = SpikeTestSupport.OwnedEvent(inspectionId, 4, InspectionEventTypes.Approved);
        var reject = SpikeTestSupport.OwnedEvent(inspectionId, 4, InspectionEventTypes.Rejected);
        _ = await Task.WhenAll(
            hostA.DispatchWorkflowOwnedAsync(approve),
            hostB.DispatchWorkflowOwnedAsync(reject));
        var terminal = await hostA.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(4, terminal.LastAppliedSequence);
        Assert.Contains(terminal.CurrentShadowState, new[] { ShadowStates.Approved, ShadowStates.Rejected });
        Assert.True(
            terminal.LastDomainEventId == approve.DomainEventId
            ^ terminal.LastDomainEventId == reject.DomainEventId);
    }

    [Fact]
    public async Task Cancellation_before_runtime_commit_does_not_advance_receipt()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created);
        await using var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.DispatchWorkflowOwnedAsync(created, cancellationToken: cancellation.Token));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => host.GetWorkflowOwnedSnapshotAsync(inspectionId));
    }

    [Theory]
    [InlineData(WorkflowOwnedFaultPoint.BeforeReceipt)]
    [InlineData(WorkflowOwnedFaultPoint.AfterValidationBeforeState)]
    public async Task Fault_before_state_leaves_incident_that_prevents_safe_retry(
        WorkflowOwnedFaultPoint faultPoint)
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created);
        var submitted = SpikeTestSupport.OwnedEvent(inspectionId, 2, InspectionEventTypes.Submitted);
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            await host.DispatchWorkflowOwnedAsync(created);
            _ = await host.DispatchWorkflowOwnedAsync(submitted, faultPoint);
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        var rolledBack = await restarted.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(1, rolledBack.LastAppliedSequence);
        Assert.Equal(created.DomainEventId, rolledBack.LastDomainEventId);
        Assert.Equal(ShadowStates.Draft, rolledBack.CurrentShadowState);
        Assert.Equal(1, rolledBack.BookmarkCount);
        Assert.NotEmpty(rolledBack.Incidents);

        Assert.Equal(WorkflowOwnedDisposition.InvalidTransition, await restarted.DispatchWorkflowOwnedAsync(submitted));
        Assert.Equal(1, (await restarted.GetWorkflowOwnedSnapshotAsync(inspectionId)).LastAppliedSequence);
    }

    [Fact]
    public async Task Fault_after_receipt_mutation_exposes_non_atomic_persisted_receipt()
    {
        var inspectionId = Guid.NewGuid();
        var created = SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created);
        var submitted = SpikeTestSupport.OwnedEvent(inspectionId, 2, InspectionEventTypes.Submitted);
        await using (var host = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString))
        {
            await host.DispatchWorkflowOwnedAsync(created);
            _ = await host.DispatchWorkflowOwnedAsync(
                submitted,
                WorkflowOwnedFaultPoint.AfterReceiptBeforeCompletion);
        }

        await using var restarted = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        var unsafeState = await restarted.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.Equal(2, unsafeState.LastAppliedSequence);
        Assert.Equal(submitted.DomainEventId, unsafeState.LastDomainEventId);
        Assert.Equal(ShadowStates.Submitted, unsafeState.CurrentShadowState);
        Assert.NotEmpty(unsafeState.Incidents);
    }

    [Fact]
    public async Task Two_hosts_classify_sequence_races_without_corrupting_the_receipt()
    {
        var inspectionId = Guid.NewGuid();
        await using var hostA = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        await using var hostB = await PersistenceSpikeHost.CreateAsync(SpikeTestSupport.ConnectionString);
        await hostA.DispatchWorkflowOwnedAsync(SpikeTestSupport.OwnedEvent(inspectionId, 1, InspectionEventTypes.Created));

        var sequence2 = SpikeTestSupport.OwnedEvent(inspectionId, 2, InspectionEventTypes.Submitted);
        var sequence3 = SpikeTestSupport.OwnedEvent(inspectionId, 3, InspectionEventTypes.StartReview);
        var adjacent = await Task.WhenAll(
            hostA.DispatchWorkflowOwnedAsync(sequence2),
            hostB.DispatchWorkflowOwnedAsync(sequence3));
        Assert.Contains(WorkflowOwnedDisposition.Applied, adjacent);
        var afterAdjacent = await hostA.GetWorkflowOwnedSnapshotAsync(inspectionId);
        Assert.InRange(afterAdjacent.LastAppliedSequence, 2, 3);
        if (afterAdjacent.LastAppliedSequence == 2)
            Assert.Equal(WorkflowOwnedDisposition.Applied, await hostA.DispatchWorkflowOwnedAsync(sequence3));

        var reassignInspectionId = Guid.NewGuid();
        await hostA.DispatchWorkflowOwnedAsync(
            SpikeTestSupport.OwnedEvent(reassignInspectionId, 1, InspectionEventTypes.Created));
        await hostA.DispatchWorkflowOwnedAsync(
            SpikeTestSupport.OwnedEvent(reassignInspectionId, 2, InspectionEventTypes.Submitted));
        var reassign = SpikeTestSupport.OwnedEvent(reassignInspectionId, 3, InspectionEventTypes.ReviewerReassigned);
        var startReview = SpikeTestSupport.OwnedEvent(reassignInspectionId, 3, InspectionEventTypes.StartReview);
        var competing = await Task.WhenAll(
            hostA.DispatchWorkflowOwnedAsync(reassign),
            hostB.DispatchWorkflowOwnedAsync(startReview));
        var final = await hostA.GetWorkflowOwnedSnapshotAsync(reassignInspectionId);
        Assert.Equal(3, final.LastAppliedSequence);
        Assert.Contains(final.LastDomainEventId, new[] { reassign.DomainEventId, startReview.DomainEventId });
        Assert.Equal(
            final.LastDomainEventId == reassign.DomainEventId ? ShadowStates.Submitted : ShadowStates.UnderReview,
            final.CurrentShadowState);
        Assert.Contains(WorkflowOwnedDisposition.Applied, competing);
        Assert.Contains(competing, x => x is WorkflowOwnedDisposition.Conflict or WorkflowOwnedDisposition.InvalidTransition or WorkflowOwnedDisposition.Applied);
    }
}
