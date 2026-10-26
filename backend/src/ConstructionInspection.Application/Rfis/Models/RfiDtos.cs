using ConstructionInspection.Domain.Rfis;

namespace ConstructionInspection.Application.Rfis.Models;

public sealed record RfiListItemDto(Guid Id, string Number, string Subject,
    Guid ConstructionObjectId, string ConstructionObjectCode, string ConstructionObjectName,
    RfiStatus Status, Guid CreatedByUserId, Guid? AssignedToUserId, string? AssignedToDisplayName,
    DateTime? DueAtUtc, bool IsOverdue, DateTime CreatedAt, DateTime? UpdatedAt,
    string RfiRowVersion, string ProcessRowVersion);
public sealed record RfiEntryDto(Guid Id, int EntryNumber, RfiEntryType EntryType,
    string Content, Guid CreatedByUserId, string CreatedByDisplayName, DateTime CreatedAtUtc);
public sealed record RfiDetailDto(Guid Id, string Number, Guid ConstructionObjectId,
    string ConstructionObjectCode, string ConstructionObjectName, string Subject, string Question,
    RfiStatus Status, Guid CreatedByUserId, Guid? AssignedToUserId, string? AssignedToDisplayName,
    DateTime? DueAtUtc, DateTime? CompletedAt, DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<RfiEntryDto> Entries, string RfiRowVersion, string ProcessRowVersion);
public sealed record RfiWorkflowResultDto(Guid RfiId, RfiStatus Status, Guid? AssignedToUserId,
    DateTime? DueAtUtc, DateTime? CompletedAt, string RfiRowVersion, string ProcessRowVersion,
    RfiEntryDto? CreatedEntry = null);
public sealed record RfiHistoryDto(Guid Id, string Action, RfiStatus? FromStatus, RfiStatus ToStatus,
    Guid ActorUserId, string ActorDisplayName, string? Reason, DateTime PerformedAt,
    Guid? RfiEntryId, Guid? PreviousAssignedToUserId, Guid? NewAssignedToUserId);
public sealed record RfiResponderOptionDto(Guid UserId, string DisplayName);
public sealed record RfiReadScope(Guid UserId, bool CanReadAll, bool IsInspector, bool IsReviewer);
public sealed record RfiQueryCriteria(int PageNumber, int PageSize, string? Search, RfiStatus? Status,
    Guid? ConstructionObjectId, bool AssignedToMe, bool? Overdue, string SortBy,
    string SortDirection, RfiReadScope Scope, DateTime UtcNow);
