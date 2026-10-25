using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ConstructionInspection.ElsaPersistenceSpike;

public sealed record WorkflowOwnedEventEnvelope(
    int PayloadVersion,
    Guid DomainEventId,
    Guid InspectionId,
    long AggregateSequence,
    string EventType,
    string PayloadHash,
    DateTimeOffset OccurredAtUtc)
{
    public static WorkflowOwnedEventEnvelope Create(
        Guid inspectionId,
        long aggregateSequence,
        string eventType,
        Guid? domainEventId = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        var id = domainEventId ?? Guid.NewGuid();
        var occurred = occurredAtUtc ?? DateTimeOffset.UtcNow;
        return new WorkflowOwnedEventEnvelope(
            1,
            id,
            inspectionId,
            aggregateSequence,
            eventType,
            ComputeHash(1, id, inspectionId, aggregateSequence, eventType, occurred),
            occurred);
    }

    public bool HasValidHash() => string.Equals(
        PayloadHash,
        ComputeHash(PayloadVersion, DomainEventId, InspectionId, AggregateSequence, EventType, OccurredAtUtc),
        StringComparison.Ordinal);

    private static string ComputeHash(
        int version,
        Guid eventId,
        Guid inspectionId,
        long sequence,
        string eventType,
        DateTimeOffset occurredAtUtc)
    {
        var canonical = string.Join('\n',
            version.ToString(CultureInfo.InvariantCulture),
            eventId.ToString("D"),
            inspectionId.ToString("D"),
            sequence.ToString(CultureInfo.InvariantCulture),
            eventType,
            occurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record WorkflowOwnedStimulus(string InspectionId);

public enum WorkflowOwnedFaultPoint
{
    None,
    BeforeReceipt,
    AfterValidationBeforeState,
    AfterReceiptBeforeCompletion
}

public enum WorkflowOwnedDisposition
{
    Applied,
    Duplicate,
    Conflict,
    Obsolete,
    Gap,
    InvalidTransition,
    TerminalConflict,
    InstanceNotFound,
    RuntimeDidNotCommit
}

public sealed record WorkflowOwnedSnapshot(
    string WorkflowInstanceId,
    string RuntimeSubStatus,
    string CurrentShadowState,
    long LastAppliedSequence,
    Guid LastDomainEventId,
    string LastPayloadHash,
    int BookmarkCount,
    IReadOnlyCollection<string> Incidents);
