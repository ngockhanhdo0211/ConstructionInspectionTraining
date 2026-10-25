using System.Text.Json;

namespace ConstructionInspection.ElsaPersistenceSpike;

public sealed record InspectionEventPayload(
    int SchemaVersion,
    Guid DomainEventId,
    string EventType,
    Guid InspectionId,
    Guid ProcessId,
    Guid ActorUserId,
    string? FromStatus,
    string ToStatus,
    Guid? SubmissionId,
    int? RevisionNumber,
    Guid? PreviousReviewerUserId,
    Guid? NewReviewerUserId);

public sealed record InspectionStimulus(string EventType, string InspectionId);

public static class InspectionEventTypes
{
    public const string Created = "Inspection.Created";
    public const string Submitted = "Inspection.Submitted";
    public const string StartReview = "Inspection.StartReview";
    public const string Approved = "Inspection.Approved";
    public const string Rejected = "Inspection.Rejected";
    public const string RecheckRequested = "Inspection.RecheckRequested";
    public const string RevisionCreated = "Inspection.RevisionCreated";
    public const string Resubmitted = "Inspection.Resubmitted";
    public const string ReviewerReassigned = "Inspection.ReviewerReassigned";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Created,
        Submitted,
        StartReview,
        Approved,
        Rejected,
        RecheckRequested,
        RevisionCreated,
        Resubmitted,
        ReviewerReassigned
    };
}

public static class SpikeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
}

public enum DispatchResult
{
    Processed,
    Duplicate,
    Reconciled,
    UnknownEvent,
    OutOfOrder,
    PayloadConflict,
    InstanceNotFound
}

public enum CrashPoint
{
    None,
    BeforeInboxCommit,
    AfterInboxCommitBeforeElsa,
    AfterElsaPersistenceBeforeInboxProcessed
}

public static class ShadowStates
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string UnderReview = "UnderReview";
    public const string Recheck = "Recheck";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
