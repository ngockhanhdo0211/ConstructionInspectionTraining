using ConstructionInspection.ElsaSpike;
using System.Diagnostics;

var timer = Stopwatch.StartNew();
await using var spike = await InspectionSpikeHost.CreateAsync();

var actorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
var processId = Guid.Parse("20000000-0000-0000-0000-000000000001");
var inspectionA = Guid.Parse("30000000-0000-0000-0000-000000000001");
var inspectionB = Guid.Parse("30000000-0000-0000-0000-000000000002");
var inspectionC = Guid.Parse("30000000-0000-0000-0000-000000000003");

await spike.StartAsync(inspectionA, processId, actorId);
await spike.StartAsync(inspectionB, processId, actorId);
await spike.StartAsync(inspectionC, processId, actorId);

await SendPathAsync(spike, inspectionA, actorId,
    InspectionEventTypes.Submitted,
    InspectionEventTypes.StartReview,
    InspectionEventTypes.Approved);

await SendPathAsync(spike, inspectionC, actorId,
    InspectionEventTypes.Submitted,
    InspectionEventTypes.StartReview,
    InspectionEventTypes.Recheck,
    InspectionEventTypes.RevisionCreated,
    InspectionEventTypes.Resubmitted,
    InspectionEventTypes.StartReview,
    InspectionEventTypes.Approved);

var stateA = await spike.GetStateAsync(inspectionA);
var stateB = await spike.GetStateAsync(inspectionB);
var stateC = await spike.GetStateAsync(inspectionC);

Console.WriteLine($"A={stateA.SubStatus}; B={stateB.SubStatus}; C={stateC.SubStatus}");
Console.WriteLine($"Active bookmarks: A={stateA.Bookmarks.Count}, B={stateB.Bookmarks.Count}, C={stateC.Bookmarks.Count}");
Console.WriteLine($"ElapsedMs={timer.ElapsedMilliseconds}; WorkingSetMiB={Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d:F1}");

static async Task SendPathAsync(
    InspectionSpikeHost spike,
    Guid inspectionId,
    Guid actorId,
    params string[] eventTypes)
{
    var revision = 1;
    foreach (var eventType in eventTypes)
    {
        if (eventType == InspectionEventTypes.RevisionCreated)
            revision++;
        var payload = new InspectionEventPayload(
            Guid.NewGuid(),
            inspectionId,
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            eventType,
            actorId,
            revision,
            DateTimeOffset.UtcNow);
        var result = await spike.SendAsync(payload);
        if (result != DispatchResult.Processed)
            throw new InvalidOperationException($"Smoke event {eventType} was not processed: {result}.");
        var state = await spike.GetStateAsync(inspectionId);
        Console.WriteLine($"{eventType}: {state.SubStatus}, bookmarks={state.Bookmarks.Count}");
    }
}
