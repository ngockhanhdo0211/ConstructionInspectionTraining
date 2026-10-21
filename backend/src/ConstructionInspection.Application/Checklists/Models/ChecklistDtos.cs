using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.Checklists.Models;

public sealed record ChecklistItemDto(Guid Id, int Sequence, string Criterion, bool IsRequired,
    ChecklistItemResult Result, string? Note, string RowVersion);
public sealed record ChecklistInspectionDto(Guid Id, string Number, string Title);
public sealed record ChecklistListItemDto(Guid Id, string Number, string Title, Guid ConstructionObjectId,
    string ConstructionObjectCode, string ConstructionObjectName, ChecklistStatus Status,
    Guid CreatedByUserId, Guid? ReviewerUserId, int ItemCount, int PassCount, int FailCount,
    int NotApplicableCount, int NotCheckedCount, DateTime CreatedAt, DateTime? UpdatedAt);
public sealed record ChecklistDetailDto(Guid Id, string Number, Guid ConstructionObjectId,
    string ConstructionObjectCode, string ConstructionObjectName, string Title, string? Description,
    ChecklistStatus Status, Guid CreatedByUserId, Guid? ReviewerUserId, Guid? CurrentSubmissionId,
    int NextSubmissionNumber, DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<ChecklistItemDto> Items, IReadOnlyList<ChecklistInspectionDto> Inspections,
    string ChecklistRowVersion, string ProcessRowVersion);
public sealed record ChecklistWorkflowResultDto(Guid ChecklistId, ChecklistStatus Status,
    Guid? ReviewerUserId, Guid? CurrentSubmissionId, int? SubmissionNumber,
    DateTime? CompletedAt, string ChecklistRowVersion, string ProcessRowVersion);
public sealed record ChecklistItemsResultDto(IReadOnlyList<ChecklistItemDto> Items,
    string ChecklistRowVersion, string ProcessRowVersion);
public sealed record ChecklistLinksResultDto(IReadOnlyList<ChecklistInspectionDto> Inspections,
    string ChecklistRowVersion, string ProcessRowVersion);
public sealed record ChecklistHistoryDto(Guid Id, string Action, ChecklistStatus? FromStatus,
    ChecklistStatus ToStatus, Guid ActorUserId, string ActorDisplayName, string? Reason,
    DateTime CreatedAt, Guid? ChecklistSubmissionId, Guid? NewReviewerUserId);
public sealed record ChecklistSubmissionListItemDto(Guid Id, int SubmissionNumber,
    Guid SubmittedByUserId, string SubmittedByDisplayName, DateTime SubmittedAt,
    int ItemCount, int InspectionCount, bool IsCurrent);
public sealed record ChecklistSubmissionDetailDto(Guid Id, int SubmissionNumber,
    Guid SubmittedByUserId, string SubmittedByDisplayName, DateTime SubmittedAt,
    IReadOnlyList<ChecklistSubmissionItemDto> Items,
    IReadOnlyList<ChecklistInspectionDto> Inspections, bool IsCurrent);
public sealed record ChecklistSubmissionItemDto(Guid SourceChecklistItemId, int Sequence,
    string Criterion, bool IsRequired, ChecklistItemResult Result, string? Note);

public sealed record ChecklistReadScope(Guid UserId, bool CanReadAll, bool IsInspector, bool IsReviewer);
public sealed record ChecklistQueryCriteria(int PageNumber, int PageSize, string? Search,
    ChecklistStatus? Status, Guid? ConstructionObjectId, Guid? ReviewerUserId,
    string SortBy, string SortDirection, ChecklistReadScope Scope);
