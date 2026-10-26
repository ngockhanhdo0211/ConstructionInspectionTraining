using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.Rfis;

namespace ConstructionInspection.Domain.Inspections;

public static class ProcessStatusMappings
{
    public static ProcessStatus ToProcessStatus(this InspectionStatus status) => status switch
    {
        InspectionStatus.Draft => ProcessStatus.Draft,
        InspectionStatus.Submitted => ProcessStatus.Submitted,
        InspectionStatus.UnderReview => ProcessStatus.UnderReview,
        InspectionStatus.Approved => ProcessStatus.Approved,
        InspectionStatus.Recheck => ProcessStatus.Recheck,
        InspectionStatus.Rejected => ProcessStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown Inspection status.")
    };

    public static InspectionStatus ToInspectionStatus(this ProcessStatus status) => status switch
    {
        ProcessStatus.Draft => InspectionStatus.Draft,
        ProcessStatus.Submitted => InspectionStatus.Submitted,
        ProcessStatus.UnderReview => InspectionStatus.UnderReview,
        ProcessStatus.Approved => InspectionStatus.Approved,
        ProcessStatus.Recheck => InspectionStatus.Recheck,
        ProcessStatus.Rejected => InspectionStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status is not valid for an Inspection process.")
    };

    public static ProcessStatus ToProcessStatus(this ChecklistStatus status) => status switch
    {
        ChecklistStatus.Draft => ProcessStatus.Draft,
        ChecklistStatus.InProgress => ProcessStatus.InProgress,
        ChecklistStatus.Submitted => ProcessStatus.Submitted,
        ChecklistStatus.UnderReview => ProcessStatus.UnderReview,
        ChecklistStatus.Approved => ProcessStatus.Approved,
        ChecklistStatus.Recheck => ProcessStatus.Recheck,
        ChecklistStatus.Rejected => ProcessStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown Checklist status.")
    };

    public static ChecklistStatus ToChecklistStatus(this ProcessStatus status) => status switch
    {
        ProcessStatus.Draft => ChecklistStatus.Draft,
        ProcessStatus.InProgress => ChecklistStatus.InProgress,
        ProcessStatus.Submitted => ChecklistStatus.Submitted,
        ProcessStatus.UnderReview => ChecklistStatus.UnderReview,
        ProcessStatus.Approved => ChecklistStatus.Approved,
        ProcessStatus.Recheck => ChecklistStatus.Recheck,
        ProcessStatus.Rejected => ChecklistStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status is not valid for a Checklist process.")
    };

    public static ProcessStatus ToProcessStatus(this RfiStatus status) => status switch
    {
        RfiStatus.Draft => ProcessStatus.Draft,
        RfiStatus.Open => ProcessStatus.Open,
        RfiStatus.Responded => ProcessStatus.Responded,
        RfiStatus.Closed => ProcessStatus.Closed,
        RfiStatus.Cancelled => ProcessStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown RFI status.")
    };

    public static RfiStatus ToRfiStatus(this ProcessStatus status) => status switch
    {
        ProcessStatus.Draft => RfiStatus.Draft,
        ProcessStatus.Open => RfiStatus.Open,
        ProcessStatus.Responded => RfiStatus.Responded,
        ProcessStatus.Closed => RfiStatus.Closed,
        ProcessStatus.Cancelled => RfiStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status is not valid for an RFI process.")
    };
}
