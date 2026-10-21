namespace ConstructionInspection.Domain.Checklists;

public sealed class Checklist
{
    private readonly List<ChecklistItem> _items = [];
    private Checklist() { }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid ConstructionObjectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ReviewerUserId { get; private set; }
    public Guid? CurrentSubmissionId { get; private set; }
    public int NextSubmissionNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyCollection<ChecklistItem> Items => _items;

    public static Checklist Create(Guid id, string number, Guid constructionObjectId, string title,
        string? description, Guid actorId, DateTime createdAtUtc,
        IReadOnlyList<(string Criterion, bool IsRequired)> items)
    {
        if (id == Guid.Empty || constructionObjectId == Guid.Empty || actorId == Guid.Empty)
            throw ChecklistRules.Error("checklist.invalid_identity", "Checklist, object and actor identifiers are required.");
        if (string.IsNullOrWhiteSpace(number) || number.Length > ChecklistRules.MaximumNumberLength)
            throw ChecklistRules.Error("checklist.invalid_number", "Checklist number is invalid.");
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrEmpty(normalizedTitle) || normalizedTitle.Length > ChecklistRules.MaximumTitleLength)
            throw ChecklistRules.Error("checklist.title_invalid", "Title is required and must not exceed 250 characters.");
        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription?.Length > ChecklistRules.MaximumDescriptionLength)
            throw ChecklistRules.Error("checklist.description_invalid", "Description must not exceed 2000 characters.");
        EnsureItemCount(items.Count);

        var checklist = new Checklist
        {
            Id = id,
            Number = number,
            ConstructionObjectId = constructionObjectId,
            Title = normalizedTitle,
            Description = normalizedDescription,
            CreatedByUserId = actorId,
            NextSubmissionNumber = 1,
            CreatedAt = ChecklistRules.EnsureUtc(createdAtUtc)
        };
        for (var index = 0; index < items.Count; index++)
            checklist._items.Add(ChecklistItem.Create(Guid.NewGuid(), id, index + 1,
                items[index].Criterion, items[index].IsRequired, actorId, createdAtUtc));
        checklist.EnsureUniqueCriteria();
        return checklist;
    }

    public void ReplaceItems(IReadOnlyCollection<ChecklistItem> items, Guid actorId, DateTime updatedAtUtc)
    {
        EnsureOwner(actorId);
        EnsureItemCount(items.Count);
        if (CurrentSubmissionId.HasValue)
        {
            var current = _items.OrderBy(x => x.Sequence).ToArray();
            var proposed = items.OrderBy(x => x.Sequence).ToArray();
            if (current.Length != proposed.Length || current.Zip(proposed).Any(pair =>
                    pair.First.Id != pair.Second.Id
                    || pair.First.Sequence != pair.Second.Sequence
                    || pair.First.Criterion != pair.Second.Criterion
                    || pair.First.IsRequired != pair.Second.IsRequired))
                throw ChecklistRules.Error("checklist.structure_immutable", "Checklist item structure is immutable after first submit.");
        }
        _items.Clear();
        _items.AddRange(items.OrderBy(x => x.Sequence));
        EnsureUniqueCriteria();
        Touch(actorId, updatedAtUtc);
    }

    public int AllocateSubmission(Guid submissionId, Guid reviewerUserId, Guid actorId, DateTime updatedAtUtc)
    {
        EnsureOwner(actorId);
        if (submissionId == Guid.Empty || reviewerUserId == Guid.Empty || NextSubmissionNumber <= 0)
            throw ChecklistRules.Error("checklist.submission_invalid", "Submission and Reviewer are required.");
        if (reviewerUserId == CreatedByUserId)
            throw ChecklistRules.Error("checklist.self_review_forbidden", "The creator cannot review their own Checklist.");
        if (ReviewerUserId.HasValue && ReviewerUserId != reviewerUserId)
            throw ChecklistRules.Error("checklist.reviewer_change_forbidden", "Re-submit must keep the assigned Reviewer.");
        foreach (var item in _items) item.ValidateForSubmit();
        var allocated = NextSubmissionNumber++;
        CurrentSubmissionId = submissionId;
        ReviewerUserId = reviewerUserId;
        Touch(actorId, updatedAtUtc);
        return allocated;
    }

    public void Touch(Guid actorId, DateTime updatedAtUtc)
    {
        EnsureOwner(actorId);
        UpdatedAt = ChecklistRules.EnsureUtc(updatedAtUtc);
    }

    private void EnsureOwner(Guid actorId)
    {
        if (actorId != CreatedByUserId)
            throw ChecklistRules.Error("checklist.not_owner", "Only the creator can change this Checklist.");
    }

    private static void EnsureItemCount(int count)
    {
        if (count is < 1 or > ChecklistRules.MaximumItems)
            throw ChecklistRules.Error("checklist.item_count_invalid", "Checklist must contain between 1 and 200 items.");
    }

    private void EnsureUniqueCriteria()
    {
        if (_items.Select(x => x.Criterion).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _items.Count)
            throw ChecklistRules.Error("checklist.item_criterion_duplicate", "Checklist item criteria must be unique.");
    }
}
