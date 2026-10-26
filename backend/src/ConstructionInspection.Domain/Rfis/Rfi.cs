namespace ConstructionInspection.Domain.Rfis;

public sealed class Rfi
{
    private readonly List<RfiEntry> entries = [];
    private Rfi() { }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid ConstructionObjectId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Question { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public int NextEntryNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyCollection<RfiEntry> Entries => entries.AsReadOnly();

    public static Rfi Create(Guid id, string number, Guid objectId, string subject, string question,
        Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || objectId == Guid.Empty || actorId == Guid.Empty)
            throw RfiRules.Error("rfi.identity_invalid", "RFI, object and actor identifiers are required.");
        return new Rfi
        {
            Id = id,
            Number = RfiRules.RequiredText(number, RfiRules.MaximumNumberLength, "rfi.number_invalid", "Number"),
            ConstructionObjectId = objectId,
            Subject = RfiRules.RequiredText(subject, RfiRules.MaximumSubjectLength, "rfi.subject_invalid", "Subject"),
            Question = RfiRules.RequiredUtf8(question, "rfi.question_invalid", "Question"),
            CreatedByUserId = actorId,
            NextEntryNumber = 1,
            CreatedAt = RfiRules.EnsureUtc(createdAtUtc)
        };
    }

    public void SaveDraft(string subject, string question, Guid actorId, DateTime updatedAtUtc)
    {
        if (actorId != CreatedByUserId)
            throw RfiRules.Error("rfi.not_creator", "Only the RFI creator can edit the Draft.");
        Subject = RfiRules.RequiredText(subject, RfiRules.MaximumSubjectLength, "rfi.subject_invalid", "Subject");
        Question = RfiRules.RequiredUtf8(question, "rfi.question_invalid", "Question");
        UpdatedByUserId = actorId;
        UpdatedAt = RfiRules.EnsureUtc(updatedAtUtc);
    }

    public RfiEntry AppendEntry(Guid entryId, RfiEntryType type, string content, Guid actorId, DateTime atUtc)
    {
        var entry = RfiEntry.Create(entryId, Id, NextEntryNumber, type, content, actorId, atUtc);
        entries.Add(entry);
        checked { NextEntryNumber++; }
        UpdatedByUserId = actorId;
        UpdatedAt = RfiRules.EnsureUtc(atUtc);
        return entry;
    }
}
