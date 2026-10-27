using ConstructionInspection.Application.Dashboard.Interfaces;
using ConstructionInspection.Application.Dashboard.Models;
using ConstructionInspection.Domain.Inspections;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class DashboardReadService(ApplicationDbContext context) : IDashboardReadService
{
    public async Task<DashboardResponseDto> GetAsync(
        DashboardReadScope scope,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var processQuery = context.BusinessProcesses.AsNoTracking();
        if (!scope.CanReadAll)
        {
            processQuery = processQuery.Where(process =>
                scope.IsInspector && process.CreatedByUserId == scope.UserId
                || scope.IsReviewer && process.AssignedToUserId == scope.UserId);
        }

        var summaryBuckets = await processQuery
            .GroupBy(process => new { process.EntityType, process.Status })
            .Select(group => new SummaryBucket(
                group.Key.EntityType,
                group.Key.Status,
                group.Count(),
                group.Sum(x => !scope.IsAdministrator && (
                    scope.IsInspector && x.CreatedByUserId == scope.UserId && (
                        x.EntityType == ProcessEntityType.Inspection
                            && (x.Status == ProcessStatus.Draft || x.Status == ProcessStatus.Recheck)
                        || x.EntityType == ProcessEntityType.Checklist
                            && (x.Status == ProcessStatus.Draft || x.Status == ProcessStatus.InProgress || x.Status == ProcessStatus.Recheck)
                        || x.EntityType == ProcessEntityType.Rfi
                            && (x.Status == ProcessStatus.Draft || x.Status == ProcessStatus.Responded))
                    || scope.IsReviewer && x.AssignedToUserId == scope.UserId && (
                        x.EntityType == ProcessEntityType.Inspection
                            && (x.Status == ProcessStatus.Submitted || x.Status == ProcessStatus.UnderReview)
                        || x.EntityType == ProcessEntityType.Checklist
                            && (x.Status == ProcessStatus.Submitted || x.Status == ProcessStatus.UnderReview)
                        || x.EntityType == ProcessEntityType.Rfi && x.Status == ProcessStatus.Open)
                    || scope.IsManager && x.EntityType == ProcessEntityType.Rfi
                        && x.Status == ProcessStatus.Open && x.DueAt != null && x.DueAt < nowUtc) ? 1 : 0),
                group.Sum(x => scope.IsInspector && x.EntityType == ProcessEntityType.Rfi
                    && x.CreatedByUserId == scope.UserId && x.Status == ProcessStatus.Open ? 1 : 0),
                group.Sum(x => scope.IsInspector && x.EntityType == ProcessEntityType.Rfi
                    && x.CreatedByUserId == scope.UserId && x.Status == ProcessStatus.Responded ? 1 : 0),
                group.Sum(x => x.EntityType == ProcessEntityType.Rfi && x.Status == ProcessStatus.Open
                    && x.DueAt != null && x.DueAt < nowUtc ? 1 : 0)))
            .ToArrayAsync(cancellationToken);

        var summaryRaw = BuildSummary(summaryBuckets);

        var queueRows = new List<QueueRow>(15);
        if (!scope.IsAdministrator && (scope.IsInspector || scope.IsReviewer))
        {
            queueRows.AddRange(await GetInspectionQueueAsync(scope, cancellationToken));
            queueRows.AddRange(await GetChecklistQueueAsync(scope, cancellationToken));
        }
        if (!scope.IsAdministrator && (scope.IsInspector || scope.IsReviewer || scope.IsManager))
            queueRows.AddRange(await GetRfiQueueAsync(scope, nowUtc, cancellationToken));

        var actionQueue = queueRows
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.DueAtUtc == null)
            .ThenBy(x => x.DueAtUtc)
            .ThenBy(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Module, StringComparer.Ordinal)
            .ThenBy(x => x.EntityId)
            .Take(15)
            .Select(row => ToDto(row, scope))
            .ToArray();

        return new DashboardResponseDto(
            nowUtc,
            scope.Roles,
            scope.ScopeMode,
            ToInspectionSummary(summaryRaw.Inspection),
            ToChecklistSummary(summaryRaw.Checklist),
            ToRfiSummary(summaryRaw.Rfi),
            actionQueue);
    }

    private async Task<IReadOnlyList<QueueRow>> GetInspectionQueueAsync(
        DashboardReadScope scope,
        CancellationToken cancellationToken)
    {
        var query =
            from process in context.BusinessProcesses.AsNoTracking()
            join inspection in context.Inspections.AsNoTracking()
                on process.InspectionId equals inspection.Id
            join constructionObject in context.ConstructionObjects.AsNoTracking()
                on inspection.ConstructionObjectId equals constructionObject.Id
            where process.EntityType == ProcessEntityType.Inspection
            select new { Process = process, Entity = inspection, ConstructionObject = constructionObject };

        query = scope.IsInspector && scope.IsReviewer
            ? query.Where(x => x.Process.CreatedByUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Draft || x.Process.Status == ProcessStatus.Recheck)
                || x.Process.AssignedToUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Submitted || x.Process.Status == ProcessStatus.UnderReview))
            : scope.IsInspector
                ? query.Where(x => x.Process.CreatedByUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Draft || x.Process.Status == ProcessStatus.Recheck))
                : query.Where(x => x.Process.AssignedToUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Submitted || x.Process.Status == ProcessStatus.UnderReview));

        return await query
            .OrderBy(x => x.Process.UpdatedAt ?? x.Entity.UpdatedAt ?? x.Process.CreatedAt)
            .ThenBy(x => x.Entity.Id)
            .Select(x => new QueueRow(
                "Inspection", x.Entity.Id, x.Entity.Number, x.Entity.Title,
                x.Process.Status, x.Process.DueAt,
                x.Process.UpdatedAt ?? x.Entity.UpdatedAt ?? x.Process.CreatedAt,
                x.ConstructionObject.Id, x.ConstructionObject.Code, x.ConstructionObject.Name, false))
            .Take(5)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<QueueRow>> GetChecklistQueueAsync(
        DashboardReadScope scope,
        CancellationToken cancellationToken)
    {
        var query =
            from process in context.BusinessProcesses.AsNoTracking()
            join checklist in context.Checklists.AsNoTracking()
                on process.ChecklistId equals checklist.Id
            join constructionObject in context.ConstructionObjects.AsNoTracking()
                on checklist.ConstructionObjectId equals constructionObject.Id
            where process.EntityType == ProcessEntityType.Checklist
            select new { Process = process, Entity = checklist, ConstructionObject = constructionObject };

        query = scope.IsInspector && scope.IsReviewer
            ? query.Where(x => x.Process.CreatedByUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Draft
                        || x.Process.Status == ProcessStatus.InProgress
                        || x.Process.Status == ProcessStatus.Recheck)
                || x.Process.AssignedToUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Submitted || x.Process.Status == ProcessStatus.UnderReview))
            : scope.IsInspector
                ? query.Where(x => x.Process.CreatedByUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Draft
                        || x.Process.Status == ProcessStatus.InProgress
                        || x.Process.Status == ProcessStatus.Recheck))
                : query.Where(x => x.Process.AssignedToUserId == scope.UserId
                    && (x.Process.Status == ProcessStatus.Submitted || x.Process.Status == ProcessStatus.UnderReview));

        return await query
            .OrderBy(x => x.Process.UpdatedAt ?? x.Entity.UpdatedAt ?? x.Process.CreatedAt)
            .ThenBy(x => x.Entity.Id)
            .Select(x => new QueueRow(
                "Checklist", x.Entity.Id, x.Entity.Number, x.Entity.Title,
                x.Process.Status, x.Process.DueAt,
                x.Process.UpdatedAt ?? x.Entity.UpdatedAt ?? x.Process.CreatedAt,
                x.ConstructionObject.Id, x.ConstructionObject.Code, x.ConstructionObject.Name, false))
            .Take(5)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<QueueRow>> GetRfiQueueAsync(
        DashboardReadScope scope,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var query =
            from process in context.BusinessProcesses.AsNoTracking()
            join rfi in context.Rfis.AsNoTracking()
                on process.RfiId equals rfi.Id
            join constructionObject in context.ConstructionObjects.AsNoTracking()
                on rfi.ConstructionObjectId equals constructionObject.Id
            let isOverdue = process.Status == ProcessStatus.Open
                && process.DueAt != null && process.DueAt < nowUtc
            where process.EntityType == ProcessEntityType.Rfi
            select new { Process = process, Entity = rfi, ConstructionObject = constructionObject, IsOverdue = isOverdue };

        query = query.Where(x =>
            scope.IsInspector && x.Process.CreatedByUserId == scope.UserId
                && (x.Process.Status == ProcessStatus.Draft || x.Process.Status == ProcessStatus.Responded)
            || scope.IsReviewer && x.Process.AssignedToUserId == scope.UserId
                && x.Process.Status == ProcessStatus.Open
            || scope.IsManager && x.IsOverdue);

        return await query
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.Process.DueAt == null)
            .ThenBy(x => x.Process.DueAt)
            .ThenBy(x => x.Process.UpdatedAt ?? x.Entity.UpdatedAt ?? x.Process.CreatedAt)
            .ThenBy(x => x.Entity.Id)
            .Select(x => new QueueRow(
                "Rfi", x.Entity.Id, x.Entity.Number, x.Entity.Subject,
                x.Process.Status, x.Process.DueAt,
                x.Process.UpdatedAt ?? x.Entity.UpdatedAt ?? x.Process.CreatedAt,
                x.ConstructionObject.Id, x.ConstructionObject.Code, x.ConstructionObject.Name, x.IsOverdue))
            .Take(5)
            .ToArrayAsync(cancellationToken);
    }

    private static InspectionDashboardSummaryDto ToInspectionSummary(InspectionSummaryRaw value) =>
        new(value.Total, value.Draft + value.Submitted + value.UnderReview + value.Recheck,
            value.AwaitingMyAction,
            new(value.Draft, value.Submitted, value.UnderReview, value.Approved,
                value.Recheck, value.Rejected));

    private static ChecklistDashboardSummaryDto ToChecklistSummary(ChecklistSummaryRaw value) =>
        new(value.Total,
            value.Draft + value.InProgress + value.Submitted + value.UnderReview + value.Recheck,
            value.AwaitingMyAction,
            new(value.Draft, value.InProgress, value.Submitted, value.UnderReview,
                value.Approved, value.Recheck, value.Rejected));

    private static RfiDashboardSummaryDto ToRfiSummary(RfiSummaryRaw value) =>
        new(value.Total, value.Draft + value.Open + value.Responded, value.AwaitingMyAction,
            value.AwaitingResponse, value.AwaitingInspectorAction, value.OverdueOpen,
            new(value.Draft, value.Open, value.Responded, value.Closed, value.Cancelled));

    private static CombinedSummaryRaw BuildSummary(IReadOnlyList<SummaryBucket> buckets)
    {
        foreach (var bucket in buckets)
        {
            var supported = bucket.EntityType switch
            {
                ProcessEntityType.Inspection => bucket.Status is ProcessStatus.Draft
                    or ProcessStatus.Submitted or ProcessStatus.UnderReview or ProcessStatus.Approved
                    or ProcessStatus.Recheck or ProcessStatus.Rejected,
                ProcessEntityType.Checklist => bucket.Status is ProcessStatus.Draft
                    or ProcessStatus.InProgress or ProcessStatus.Submitted or ProcessStatus.UnderReview
                    or ProcessStatus.Approved or ProcessStatus.Recheck or ProcessStatus.Rejected,
                ProcessEntityType.Rfi => bucket.Status is ProcessStatus.Draft or ProcessStatus.Open
                    or ProcessStatus.Responded or ProcessStatus.Closed or ProcessStatus.Cancelled,
                _ => false
            };
            if (!supported)
                throw new InvalidOperationException(
                    "Dashboard summary encountered an unsupported process entity type or status.");
        }

        int Count(ProcessEntityType entityType, ProcessStatus status) => buckets
            .Where(x => x.EntityType == entityType && x.Status == status)
            .Sum(x => x.Count);
        int Total(ProcessEntityType entityType) => buckets
            .Where(x => x.EntityType == entityType)
            .Sum(x => x.Count);
        int Metric(ProcessEntityType entityType, Func<SummaryBucket, int> selector) => buckets
            .Where(x => x.EntityType == entityType)
            .Sum(selector);

        return new(
            buckets.Sum(x => x.Count),
            new(
                Total(ProcessEntityType.Inspection),
                Count(ProcessEntityType.Inspection, ProcessStatus.Draft),
                Count(ProcessEntityType.Inspection, ProcessStatus.Submitted),
                Count(ProcessEntityType.Inspection, ProcessStatus.UnderReview),
                Count(ProcessEntityType.Inspection, ProcessStatus.Approved),
                Count(ProcessEntityType.Inspection, ProcessStatus.Recheck),
                Count(ProcessEntityType.Inspection, ProcessStatus.Rejected),
                Metric(ProcessEntityType.Inspection, x => x.AwaitingMyAction)),
            new(
                Total(ProcessEntityType.Checklist),
                Count(ProcessEntityType.Checklist, ProcessStatus.Draft),
                Count(ProcessEntityType.Checklist, ProcessStatus.InProgress),
                Count(ProcessEntityType.Checklist, ProcessStatus.Submitted),
                Count(ProcessEntityType.Checklist, ProcessStatus.UnderReview),
                Count(ProcessEntityType.Checklist, ProcessStatus.Approved),
                Count(ProcessEntityType.Checklist, ProcessStatus.Recheck),
                Count(ProcessEntityType.Checklist, ProcessStatus.Rejected),
                Metric(ProcessEntityType.Checklist, x => x.AwaitingMyAction)),
            new(
                Total(ProcessEntityType.Rfi),
                Count(ProcessEntityType.Rfi, ProcessStatus.Draft),
                Count(ProcessEntityType.Rfi, ProcessStatus.Open),
                Count(ProcessEntityType.Rfi, ProcessStatus.Responded),
                Count(ProcessEntityType.Rfi, ProcessStatus.Closed),
                Count(ProcessEntityType.Rfi, ProcessStatus.Cancelled),
                Metric(ProcessEntityType.Rfi, x => x.AwaitingMyAction),
                Metric(ProcessEntityType.Rfi, x => x.AwaitingResponse),
                Metric(ProcessEntityType.Rfi, x => x.AwaitingInspectorAction),
                Metric(ProcessEntityType.Rfi, x => x.OverdueOpen)));
    }

    private static DashboardActionItemDto ToDto(QueueRow row, DashboardReadScope scope) => new(
        row.Module,
        row.EntityId,
        row.Number,
        row.DisplayTitle,
        StatusName(row.Module, row.Status),
        ActionKind(row.Module, row.Status, row.IsOverdue, scope),
        row.DueAtUtc,
        row.UpdatedAtUtc,
        new(row.ConstructionObjectId, row.ConstructionObjectCode, row.ConstructionObjectName),
        row.IsOverdue);

    private static string StatusName(string module, ProcessStatus status) => module switch
    {
        "Inspection" => status.ToInspectionStatus().ToString(),
        "Checklist" => status.ToChecklistStatus().ToString(),
        "Rfi" => status.ToRfiStatus().ToString(),
        _ => throw new InvalidOperationException("Unknown Dashboard module.")
    };

    private static string ActionKind(
        string module,
        ProcessStatus status,
        bool isOverdue,
        DashboardReadScope scope) =>
        (module, status, isOverdue) switch
        {
            ("Inspection", ProcessStatus.Draft or ProcessStatus.Recheck, _) => "Edit",
            ("Inspection", ProcessStatus.Submitted, _) => "StartReview",
            ("Inspection", ProcessStatus.UnderReview, _) => "Review",
            ("Checklist", ProcessStatus.Draft, _) => "Start",
            ("Checklist", ProcessStatus.InProgress, _) => "Edit",
            ("Checklist", ProcessStatus.Recheck, _) => "Resume",
            ("Checklist", ProcessStatus.Submitted, _) => "StartReview",
            ("Checklist", ProcessStatus.UnderReview, _) => "Review",
            ("Rfi", ProcessStatus.Draft, _) => "Edit",
            ("Rfi", ProcessStatus.Open, true) when scope.IsManager && !scope.IsReviewer => "Reassign",
            ("Rfi", ProcessStatus.Open, _) => "Respond",
            ("Rfi", ProcessStatus.Responded, _) => "ReviewResponse",
            _ => throw new InvalidOperationException("Unsupported Dashboard action state.")
        };

    private sealed record InspectionSummaryRaw(
        int Total, int Draft, int Submitted, int UnderReview, int Approved,
        int Recheck, int Rejected, int AwaitingMyAction);

    private sealed record ChecklistSummaryRaw(
        int Total, int Draft, int InProgress, int Submitted, int UnderReview,
        int Approved, int Recheck, int Rejected, int AwaitingMyAction);

    private sealed record RfiSummaryRaw(
        int Total, int Draft, int Open, int Responded, int Closed, int Cancelled,
        int AwaitingMyAction, int AwaitingResponse, int AwaitingInspectorAction,
        int OverdueOpen);

    private sealed record CombinedSummaryRaw(
        int Total,
        InspectionSummaryRaw Inspection,
        ChecklistSummaryRaw Checklist,
        RfiSummaryRaw Rfi);

    private sealed record SummaryBucket(
        ProcessEntityType EntityType,
        ProcessStatus Status,
        int Count,
        int AwaitingMyAction,
        int AwaitingResponse,
        int AwaitingInspectorAction,
        int OverdueOpen);

    private sealed record QueueRow(
        string Module,
        Guid EntityId,
        string Number,
        string DisplayTitle,
        ProcessStatus Status,
        DateTime? DueAtUtc,
        DateTime UpdatedAtUtc,
        Guid ConstructionObjectId,
        string ConstructionObjectCode,
        string ConstructionObjectName,
        bool IsOverdue);
}
