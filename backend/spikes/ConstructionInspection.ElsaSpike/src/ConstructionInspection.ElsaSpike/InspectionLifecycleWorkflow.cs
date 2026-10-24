using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace ConstructionInspection.ElsaSpike;

public sealed class InspectionLifecycleWorkflow : WorkflowBase
{
    public const string DefinitionId = "inspection-lifecycle-e0";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Root = new WaitForInspectionLifecycle();
    }
}

[Activity("E0", "Inspection", "Waits for fake Inspection lifecycle events.")]
public sealed class WaitForInspectionLifecycle : Activity
{
    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        WaitFor(context, InspectionEventTypes.Submitted, OnSubmittedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnSubmittedAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.StartReview, OnStartReviewAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnStartReviewAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.Approved, OnTerminalAsync);
        WaitFor(context, InspectionEventTypes.Rejected, OnTerminalAsync);
        WaitFor(context, InspectionEventTypes.Recheck, OnRecheckAsync);
        WaitFor(context, InspectionEventTypes.Reassigned, OnReassignedAsync);
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnTerminalAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        await context.CompleteActivityAsync();
    }

    private ValueTask OnRecheckAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.RevisionCreated, OnRevisionCreatedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnRevisionCreatedAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.Resubmitted, OnResubmittedAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnResubmittedAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.StartReview, OnStartReviewAsync);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnReassignedAsync(ActivityExecutionContext context)
    {
        Record(context);
        context.ClearBookmarks();
        WaitFor(context, InspectionEventTypes.StartReview, OnStartReviewAsync);
        return ValueTask.CompletedTask;
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

    private static void Record(ActivityExecutionContext context)
    {
        var payload = context.GetWorkflowInput<InspectionEventPayload>("Event")
            ?? throw new InvalidOperationException("Resume event is required.");
        context.GetRequiredService<SpikeExecutionProbe>().Record(payload, context.CancellationToken);
    }
}
