namespace ConstructionInspection.Domain.Rfis;

public sealed class RfiEntry
{
    private RfiEntry() { }

    public Guid Id { get; private set; }
    public Guid RfiId { get; private set; }
    public int EntryNumber { get; private set; }
    public RfiEntryType EntryType { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static RfiEntry Create(Guid id, Guid rfiId, int number, RfiEntryType type,
        string content, Guid actorId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || rfiId == Guid.Empty || actorId == Guid.Empty || number < 1)
            throw RfiRules.Error("rfi.entry_identity_invalid", "Valid RFI entry identifiers are required.");
        return new RfiEntry
        {
            Id = id,
            RfiId = rfiId,
            EntryNumber = number,
            EntryType = type,
            Content = RfiRules.RequiredUtf8(content, "rfi.entry_content_invalid", "Entry content"),
            CreatedByUserId = actorId,
            CreatedAtUtc = RfiRules.EnsureUtc(createdAtUtc)
        };
    }
}
