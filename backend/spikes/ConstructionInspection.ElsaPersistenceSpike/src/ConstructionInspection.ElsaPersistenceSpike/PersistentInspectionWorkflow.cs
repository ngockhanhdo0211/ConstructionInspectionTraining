using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace ConstructionInspection.ElsaPersistenceSpike;

public sealed class PersistentInspectionWorkflow : WorkflowBase
{
    public const string DefinitionId = "inspection-lifecycle-e1-sql-spike";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Root = new WaitForPersistentInspectionLifecycle();
    }
}

[Activity("E1Spike", "Inspection", "Persists the controlled Inspection shadow state for the SQL compatibility spike.")]
public sealed class WaitForPersistentInspectionLifecycle : Activity
{
    public const string ShadowStateProperty = "ShadowState";
    public const string LastDomainEventIdProperty = "LastDomainEventId";
    public const string LastEventTypeProperty = "LastEventType";

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var created = RequiredEvent(context);
        if (created.EventType != InspectionEventTypes.Created)
            throw new InvalidOperationException("The first workflow event must be Inspection.Created.");
        Record(context, created, ShadowStates.Draft);
        WaitFor(context, InspectionEventTypes.Submitted, OnSubmittedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnSubmittedAsync(ActivityExecutionContext context)
    {
        Record(context, RequiredEvent(context), ShadowStates.Submitted);
        context.ClearBookmarks();
        WaitForSubmittedActions(context);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnStartReviewAsync(ActivityExecutionContext context)
    {
        Record(context, RequiredEvent(context), ShadowStates.UnderReview);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.Approved, OnApprovedAsync);
        WaitFor(context, InspectionEventTypes.Rejected, OnRejectedAsync);
        WaitFor(context, InspectionEventTypes.RecheckRequested, OnRecheckAsync);
        WaitFor(context, InspectionEventTypes.ReviewerReassigned, OnReassignedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnApprovedAsync(ActivityExecutionContext context) => CompleteAsync(context, ShadowStates.Approved);

    private ValueTask OnRejectedAsync(ActivityExecutionContext context) => CompleteAsync(context, ShadowStates.Rejected);

    private async ValueTask CompleteAsync(ActivityExecutionContext context, string state)
    {
        Record(context, RequiredEvent(context), state);
        context.ClearBookmarks();
        await context.CompleteActivityAsync();
    }

    private ValueTask OnRecheckAsync(ActivityExecutionContext context)
    {
        Record(context, RequiredEvent(context), ShadowStates.Recheck);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.RevisionCreated, OnRevisionCreatedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnRevisionCreatedAsync(ActivityExecutionContext context)
    {
        Record(context, RequiredEvent(context), ShadowStates.Recheck);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.Resubmitted, OnResubmittedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnResubmittedAsync(ActivityExecutionContext context)
    {
        Record(context, RequiredEvent(context), ShadowStates.Submitted);
        context.ClearBookmarks();
        WaitForSubmittedActions(context);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnReassignedAsync(ActivityExecutionContext context)
    {
        Record(context, RequiredEvent(context), ShadowStates.Submitted);
        context.ClearBookmarks();
        WaitForSubmittedActions(context);
        return ValueTask.CompletedTask;
    }

    private void WaitForSubmittedActions(ActivityExecutionContext context)
    {
        WaitFor(context, InspectionEventTypes.StartReview, OnStartReviewAsync);
        WaitFor(context, InspectionEventTypes.ReviewerReassigned, OnReassignedAsync);
    }

    private static void WaitFor(
        ActivityExecutionContext context,
        string eventType,
        ExecuteActivityDelegate callback)
    {
        var inspectionId = context.GetWorkflowInput<string>("InspectionId")
            ?? throw new InvalidOperationException("InspectionId is required.");
        context.CreateBookmark(new CreateBookmarkArgs
        {
            Stimulus = new InspectionStimulus(eventType, inspectionId),
            Callback = callback,
            IncludeActivityInstanceId = false,
            AutoBurn = true,
            AutoComplete = false
        });
    }

    private static InspectionEventPayload RequiredEvent(ActivityExecutionContext context) =>
        context.GetWorkflowInput<InspectionEventPayload>("Event")
        ?? throw new InvalidOperationException("The workflow event is required.");

    private static void Record(
        ActivityExecutionContext context,
        InspectionEventPayload payload,
        string shadowState)
    {
        context.WorkflowExecutionContext.SetProperty(ShadowStateProperty, shadowState);
        context.WorkflowExecutionContext.SetProperty(LastDomainEventIdProperty, payload.DomainEventId.ToString("D"));
        context.WorkflowExecutionContext.SetProperty(LastEventTypeProperty, payload.EventType);
    }
}
