namespace ConstructionInspection.ElsaSpike;

public sealed record InspectionEventPayload(
    Guid DomainEventId,
    Guid InspectionId,
    Guid ProcessId,
    string EventType,
    Guid ActorUserId,
    int? SubmissionRevisionNumber,
    DateTimeOffset OccurredAt);

public sealed record InspectionStimulus(string EventType, string InspectionId);

public static class InspectionEventTypes
{
    public const string Submitted = "Inspection.Submitted";
    public const string StartReview = "Inspection.StartReview";
    public const string Approved = "Inspection.Approved";
    public const string Rejected = "Inspection.Rejected";
    public const string Recheck = "Inspection.RecheckRequested";
    public const string RevisionCreated = "Inspection.RevisionCreated";
    public const string Resubmitted = "Inspection.Resubmitted";
    public const string Reassigned = "Inspection.Reassigned";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Submitted,
        StartReview,
        Approved,
        Rejected,
        Recheck,
        RevisionCreated,
        Resubmitted,
        Reassigned
    };
}
