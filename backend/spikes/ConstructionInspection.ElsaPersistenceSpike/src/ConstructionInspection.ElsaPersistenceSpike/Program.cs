using System.Diagnostics;
using ConstructionInspection.ElsaPersistenceSpike;

var connectionString = SpikeDatabaseSafety.RequireSafeConnectionString(
    Environment.GetEnvironmentVariable("ConnectionStrings__ElsaSpikeConnection"));
if (args.Length == 0)
    throw new InvalidOperationException("A spike command is required.");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
var cancellationToken = shutdown.Token;
var timer = Stopwatch.StartNew();
var command = args[0].ToLowerInvariant();
await using var host = await PersistenceSpikeHost.CreateAsync(
    connectionString,
    runMigrations: command == "migrate",
    cancellationToken);

switch (command)
{
    case "migrate":
        Console.WriteLine("SPIKE_SCHEMA_READY");
        break;
    case "send":
        {
            var inspectionId = Guid.Parse(args[1]);
            var domainEventId = Guid.Parse(args[2]);
            var eventType = args[3];
            var revision = args.Length > 4 ? int.Parse(args[4]) : 1;
            var crashPoint = args.Length > 5 ? Enum.Parse<CrashPoint>(args[5], true) : CrashPoint.None;
            var result = await host.DispatchAsync(
                Event(inspectionId, domainEventId, eventType, revision),
                crashPoint,
                cancellationToken);
            Console.WriteLine($"RESULT={result}");
            break;
        }
    case "reconcile":
        {
            var result = await host.ReconcileAsync(Guid.Parse(args[1]), cancellationToken);
            Console.WriteLine($"RESULT={result}");
            break;
        }
    case "state":
        {
            var snapshot = await host.GetSnapshotAsync(Guid.Parse(args[1]), cancellationToken);
            Console.WriteLine($"STATE={snapshot.ShadowState};RUNTIME={snapshot.RuntimeSubStatus};BOOKMARKS={snapshot.BookmarkEvents.Count};LAST_EVENT_PRESENT={snapshot.LastDomainEventId.HasValue}");
            break;
        }
    case "inventory":
        {
            var inventory = await SpikePersistenceDiagnostics.ReadInventoryAsync(connectionString, cancellationToken);
            Console.WriteLine($"TABLES={string.Join(',', inventory.Tables)}");
            Console.WriteLine($"DATABASE_SIZE_BYTES={inventory.DatabaseSizeBytes};USED_TABLE_BYTES={inventory.UsedTableBytes};INBOX_ROWS={inventory.InboxMessageCount};MAPPINGS={inventory.WorkflowMappingCount}");
            break;
        }
    default:
        throw new InvalidOperationException("Unknown spike command.");
}

Console.WriteLine($"ELAPSED_MS={timer.ElapsedMilliseconds};WORKING_SET_MIB={Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d:F1}");

static InspectionEventPayload Event(Guid inspectionId, Guid domainEventId, string eventType, int revision)
{
    var (from, to) = eventType switch
    {
        InspectionEventTypes.Created => ((string?)null, ShadowStates.Draft),
        InspectionEventTypes.Submitted => (ShadowStates.Draft, ShadowStates.Submitted),
        InspectionEventTypes.StartReview => (ShadowStates.Submitted, ShadowStates.UnderReview),
        InspectionEventTypes.Approved => (ShadowStates.UnderReview, ShadowStates.Approved),
        InspectionEventTypes.Rejected => (ShadowStates.UnderReview, ShadowStates.Rejected),
        InspectionEventTypes.RecheckRequested => (ShadowStates.UnderReview, ShadowStates.Recheck),
        InspectionEventTypes.RevisionCreated => (ShadowStates.Recheck, ShadowStates.Recheck),
        InspectionEventTypes.Resubmitted => (ShadowStates.Recheck, ShadowStates.Submitted),
        InspectionEventTypes.ReviewerReassigned => (ShadowStates.UnderReview, ShadowStates.Submitted),
        _ => (null, string.Empty)
    };
    return new InspectionEventPayload(
        1,
        domainEventId,
        eventType,
        inspectionId,
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        from,
        to,
        Guid.NewGuid(),
        revision,
        null,
        null);
}
