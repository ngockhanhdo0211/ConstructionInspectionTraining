namespace ConstructionInspection.Domain.FormTemplates;

public sealed class FormTemplate
{
    private FormTemplate() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public FormTemplateStatus Status { get; private set; }
    public int NextVersionNumber { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? DeactivatedByUserId { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static FormTemplate Create(
        Guid id,
        string code,
        string name,
        string? description,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        ValidateIdentity(id, createdByUserId);
        var normalizedCode = FormTemplateRules.NormalizeCode(code);
        if (!FormTemplateRules.IsValidCode(normalizedCode))
            throw Error("form_template.invalid_code", "Code format is invalid.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > FormTemplateRules.MaximumNameLength)
            throw Error("form_template.invalid_name", "Name is required and exceeds the allowed length.");
        if (description?.Trim().Length > FormTemplateRules.MaximumDescriptionLength)
            throw Error("form_template.invalid_description", "Description exceeds the allowed length.");

        return new FormTemplate
        {
            Id = id,
            Code = normalizedCode,
            Name = name.Trim(),
            Description = NormalizeOptional(description),
            Status = FormTemplateStatus.Active,
            NextVersionNumber = 2,
            CreatedByUserId = createdByUserId,
            CreatedAt = EnsureUtc(createdAtUtc)
        };
    }

    public int AllocateNextVersion(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureActive();
        ValidateActor(actorId);
        var allocated = NextVersionNumber;
        checked { NextVersionNumber++; }
        Touch(actorId, occurredAtUtc);
        return allocated;
    }

    public void MarkPublished(Guid actorId, DateTime occurredAtUtc)
    {
        EnsureActive();
        ValidateActor(actorId);
        Touch(actorId, occurredAtUtc);
    }

    public void Deactivate(Guid actorId, DateTime occurredAtUtc)
    {
        if (Status != FormTemplateStatus.Active)
            throw Error("form_template.already_inactive", "The Form Template is already inactive.");
        ValidateActor(actorId);
        var utc = EnsureUtc(occurredAtUtc);
        Status = FormTemplateStatus.Inactive;
        UpdatedByUserId = actorId;
        UpdatedAt = utc;
        DeactivatedByUserId = actorId;
        DeactivatedAt = utc;
    }

    public void EnsureActive()
    {
        if (Status != FormTemplateStatus.Active)
            throw Error("form_template.template_inactive", "The Form Template is inactive.");
    }

    private void Touch(Guid actorId, DateTime occurredAtUtc)
    {
        UpdatedByUserId = actorId;
        UpdatedAt = EnsureUtc(occurredAtUtc);
    }

    private static void ValidateIdentity(Guid id, Guid actorId)
    {
        if (id == Guid.Empty || actorId == Guid.Empty)
            throw Error("form_template.invalid_identity", "Template and actor identifiers are required.");
    }

    private static void ValidateActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
            throw Error("form_template.invalid_actor", "An actor identifier is required.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static FormTemplateDomainException Error(string code, string message) => new(code, message);
}
