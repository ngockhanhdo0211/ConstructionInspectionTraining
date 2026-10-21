namespace ConstructionInspection.Domain.Checklists;

public sealed class ChecklistItem
{
    private ChecklistItem() { }

    public Guid Id { get; private set; }
    public Guid ChecklistId { get; private set; }
    public int Sequence { get; private set; }
    public string Criterion { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }
    public ChecklistItemResult Result { get; private set; }
    public string? Note { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static ChecklistItem Create(
        Guid id, Guid checklistId, int sequence, string criterion, bool isRequired,
        Guid actorId, DateTime createdAtUtc)
    {
        ValidateIdentity(id, checklistId, actorId);
        var normalized = NormalizeCriterion(criterion);
        if (sequence <= 0) throw ChecklistRules.Error("checklist.item_sequence_invalid", "Item sequence must be positive.");
        return new ChecklistItem
        {
            Id = id,
            ChecklistId = checklistId,
            Sequence = sequence,
            Criterion = normalized,
            IsRequired = isRequired,
            Result = ChecklistItemResult.NotChecked,
            CreatedByUserId = actorId,
            CreatedAt = ChecklistRules.EnsureUtc(createdAtUtc)
        };
    }

    public void Author(int sequence, string criterion, bool isRequired, ChecklistItemResult result,
        string? note, Guid actorId, DateTime updatedAtUtc)
    {
        if (sequence <= 0 || actorId == Guid.Empty)
            throw ChecklistRules.Error("checklist.item_invalid", "A valid item sequence and actor are required.");
        Sequence = sequence;
        Criterion = NormalizeCriterion(criterion);
        IsRequired = isRequired;
        SetResult(result, note, actorId, updatedAtUtc);
    }

    public void UpdateResult(ChecklistItemResult result, string? note, Guid actorId, DateTime updatedAtUtc) =>
        SetResult(result, note, actorId, updatedAtUtc);

    public void ValidateForSubmit()
    {
        if (Result == ChecklistItemResult.NotChecked)
            throw ChecklistRules.Error("checklist.item_not_checked", "Every Checklist item must be completed before submit.");
        ValidateNote(Result, Note);
    }

    private void SetResult(ChecklistItemResult result, string? note, Guid actorId, DateTime updatedAtUtc)
    {
        if (!Enum.IsDefined(result) || actorId == Guid.Empty)
            throw ChecklistRules.Error("checklist.item_result_invalid", "A valid item result and actor are required.");
        var normalized = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ValidateNote(result, normalized);
        Result = result;
        Note = normalized;
        UpdatedByUserId = actorId;
        UpdatedAt = ChecklistRules.EnsureUtc(updatedAtUtc);
    }

    private static void ValidateNote(ChecklistItemResult result, string? note)
    {
        if (note?.Length > ChecklistRules.MaximumNoteLength)
            throw ChecklistRules.Error("checklist.item_note_invalid", "Item note exceeds the allowed length.");
        if (result is ChecklistItemResult.Fail or ChecklistItemResult.NotApplicable && note is null)
            throw ChecklistRules.Error("checklist.item_note_required", "Fail and NotApplicable require a note.");
    }

    private static string NormalizeCriterion(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > ChecklistRules.MaximumCriterionLength)
            throw ChecklistRules.Error("checklist.item_criterion_invalid", "Criterion is required and must not exceed 1000 characters.");
        return normalized;
    }

    private static void ValidateIdentity(Guid id, Guid checklistId, Guid actorId)
    {
        if (id == Guid.Empty || checklistId == Guid.Empty || actorId == Guid.Empty)
            throw ChecklistRules.Error("checklist.invalid_identity", "Checklist item identifiers are required.");
    }
}
