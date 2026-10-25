using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace ConstructionInspection.ElsaPersistenceSpike;

public sealed class WorkflowOwnedReceiptWorkflow : WorkflowBase
{
    public const string DefinitionId = "inspection-workflow-owned-receipt-spike";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Root = new WaitForWorkflowOwnedInspectionEvent();
    }
}

[Activity("AtomicitySpike", "Inspection", "Owns the durable receipt and shadow transition in one Elsa execution burst.")]
public sealed class WaitForWorkflowOwnedInspectionEvent : Activity
{
    public const string LastAppliedSequenceProperty = "LastAppliedSequence";
    public const string LastDomainEventIdProperty = "LastDomainEventId";
    public const string LastPayloadHashProperty = "LastPayloadHash";
    public const string CurrentShadowStateProperty = "CurrentShadowState";
    public const string BookmarkName = "InspectionWorkflowOwnedEvent";

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var envelope = RequiredEnvelope(context);
        ValidateEnvelope(envelope);
        if (envelope.AggregateSequence != 1 || envelope.EventType != InspectionEventTypes.Created)
            throw new InvalidOperationException("The first event must be sequence 1 Inspection.Created.");

        ThrowIfRequested(context, WorkflowOwnedFaultPoint.BeforeReceipt);
        ThrowIfRequested(context, WorkflowOwnedFaultPoint.AfterValidationBeforeState);
        Apply(context, envelope, ShadowStates.Draft);
        ThrowIfRequested(context, WorkflowOwnedFaultPoint.AfterReceiptBeforeCompletion);
        Wait(context);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnEventAsync(ActivityExecutionContext context)
    {
        var envelope = RequiredEnvelope(context);
        ThrowIfRequested(context, WorkflowOwnedFaultPoint.BeforeReceipt);
        ValidateEnvelope(envelope);

        var lastSequence = context.WorkflowExecutionContext.GetProperty<long>(LastAppliedSequenceProperty);
        var lastEventId = context.WorkflowExecutionContext.GetProperty<string>(LastDomainEventIdProperty) ?? string.Empty;
        var lastHash = context.WorkflowExecutionContext.GetProperty<string>(LastPayloadHashProperty) ?? string.Empty;
        var currentState = context.WorkflowExecutionContext.GetProperty<string>(CurrentShadowStateProperty) ?? string.Empty;

        if (envelope.AggregateSequence == lastSequence)
        {
            // Exact duplicates are a logical no-op. Same-sequence mismatches are rejected without mutation.
            _ = envelope.DomainEventId.ToString("D") == lastEventId && envelope.PayloadHash == lastHash;
            Wait(context);
            return ValueTask.CompletedTask;
        }

        if (envelope.AggregateSequence < lastSequence || envelope.AggregateSequence > lastSequence + 1)
        {
            Wait(context);
            return ValueTask.CompletedTask;
        }

        if (!TryTransition(currentState, envelope.EventType, out var nextState))
        {
            Wait(context);
            return ValueTask.CompletedTask;
        }

        ThrowIfRequested(context, WorkflowOwnedFaultPoint.AfterValidationBeforeState);
        Apply(context, envelope, nextState);
        ThrowIfRequested(context, WorkflowOwnedFaultPoint.AfterReceiptBeforeCompletion);
        Wait(context);
        return ValueTask.CompletedTask;
    }

    private void Wait(ActivityExecutionContext context)
    {
        var inspectionId = context.GetWorkflowInput<string>("InspectionId")
            ?? throw new InvalidOperationException("InspectionId is required.");
        context.CreateBookmark(new CreateBookmarkArgs
        {
            BookmarkName = BookmarkName,
            Stimulus = new WorkflowOwnedStimulus(inspectionId),
            Callback = OnEventAsync,
            IncludeActivityInstanceId = false,
            AutoBurn = true,
            AutoComplete = false
        });
    }

    private static void Apply(
        ActivityExecutionContext context,
        WorkflowOwnedEventEnvelope envelope,
        string state)
    {
        context.WorkflowExecutionContext.SetProperty(LastAppliedSequenceProperty, envelope.AggregateSequence);
        context.WorkflowExecutionContext.SetProperty(LastDomainEventIdProperty, envelope.DomainEventId.ToString("D"));
        context.WorkflowExecutionContext.SetProperty(LastPayloadHashProperty, envelope.PayloadHash);
        context.WorkflowExecutionContext.SetProperty(CurrentShadowStateProperty, state);
    }

    private static void ValidateEnvelope(WorkflowOwnedEventEnvelope envelope)
    {
        if (envelope.PayloadVersion != 1
            || envelope.DomainEventId == Guid.Empty
            || envelope.InspectionId == Guid.Empty
            || envelope.AggregateSequence < 1
            || !InspectionEventTypes.Known.Contains(envelope.EventType)
            || !envelope.HasValidHash())
            throw new InvalidOperationException("The workflow-owned event envelope is invalid.");
    }

    private static bool TryTransition(string state, string eventType, out string nextState)
    {
        nextState = state;
        switch (state, eventType)
        {
            case (ShadowStates.Draft, InspectionEventTypes.Submitted):
                nextState = ShadowStates.Submitted;
                return true;
            case (ShadowStates.Submitted, InspectionEventTypes.StartReview):
                nextState = ShadowStates.UnderReview;
                return true;
            case (ShadowStates.Submitted, InspectionEventTypes.ReviewerReassigned):
                return true;
            case (ShadowStates.UnderReview, InspectionEventTypes.ReviewerReassigned):
                nextState = ShadowStates.Submitted;
                return true;
            case (ShadowStates.UnderReview, InspectionEventTypes.Approved):
                nextState = ShadowStates.Approved;
                return true;
            case (ShadowStates.UnderReview, InspectionEventTypes.Rejected):
                nextState = ShadowStates.Rejected;
                return true;
            case (ShadowStates.UnderReview, InspectionEventTypes.RecheckRequested):
                nextState = ShadowStates.Recheck;
                return true;
            case (ShadowStates.Recheck, InspectionEventTypes.RevisionCreated):
                return true;
            case (ShadowStates.Recheck, InspectionEventTypes.Resubmitted):
                nextState = ShadowStates.Submitted;
                return true;
            default:
                return false;
        }
    }

    private static WorkflowOwnedEventEnvelope RequiredEnvelope(ActivityExecutionContext context) =>
        context.GetWorkflowInput<WorkflowOwnedEventEnvelope>("Event")
        ?? throw new InvalidOperationException("The workflow-owned event is required.");

    private static void ThrowIfRequested(ActivityExecutionContext context, WorkflowOwnedFaultPoint point)
    {
        var requested = context.GetWorkflowInput<WorkflowOwnedFaultPoint>("FaultPoint");
        if (requested == point)
            throw new WorkflowOwnedFaultException(point);
    }
}

public sealed class WorkflowOwnedFaultException(WorkflowOwnedFaultPoint point)
    : Exception($"Injected workflow-owned fault at {point}.");
