namespace ConstructionInspection.Domain.Checklists;

public enum ChecklistStatus
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    Approved = 3,
    Recheck = 4,
    Rejected = 5,
    InProgress = 6
}

public enum ChecklistItemResult
{
    NotChecked,
    Pass,
    Fail,
    NotApplicable
}
