namespace ConstructionInspection.Application.Dashboard.Models;

public sealed record InspectionDashboardStatusCountsDto(
    int Draft,
    int Submitted,
    int UnderReview,
    int Approved,
    int Recheck,
    int Rejected);

public sealed record ChecklistDashboardStatusCountsDto(
    int Draft,
    int InProgress,
    int Submitted,
    int UnderReview,
    int Approved,
    int Recheck,
    int Rejected);

public sealed record RfiDashboardStatusCountsDto(
    int Draft,
    int Open,
    int Responded,
    int Closed,
    int Cancelled);

public sealed record InspectionDashboardSummaryDto(
    int Total,
    int OpenWork,
    int AwaitingMyAction,
    InspectionDashboardStatusCountsDto StatusCounts);

public sealed record ChecklistDashboardSummaryDto(
    int Total,
    int OpenWork,
    int AwaitingMyAction,
    ChecklistDashboardStatusCountsDto StatusCounts);

public sealed record RfiDashboardSummaryDto(
    int Total,
    int OpenWork,
    int AwaitingMyAction,
    int AwaitingResponse,
    int AwaitingInspectorAction,
    int OverdueOpen,
    RfiDashboardStatusCountsDto StatusCounts);

public sealed record DashboardConstructionObjectDto(Guid Id, string Code, string Name);

public sealed record DashboardActionItemDto(
    string Module,
    Guid EntityId,
    string Number,
    string DisplayTitle,
    string Status,
    string ActionKind,
    DateTime? DueAtUtc,
    DateTime UpdatedAtUtc,
    DashboardConstructionObjectDto ConstructionObject,
    bool IsOverdue);

public sealed record DashboardResponseDto(
    DateTime GeneratedAtUtc,
    IReadOnlyList<string> Roles,
    string ScopeMode,
    InspectionDashboardSummaryDto InspectionSummary,
    ChecklistDashboardSummaryDto ChecklistSummary,
    RfiDashboardSummaryDto RfiSummary,
    IReadOnlyList<DashboardActionItemDto> ActionQueue);

public sealed record DashboardReadScope(
    Guid UserId,
    bool CanReadAll,
    bool IsInspector,
    bool IsReviewer,
    bool IsManager,
    bool IsAdministrator,
    IReadOnlyList<string> Roles,
    string ScopeMode);
