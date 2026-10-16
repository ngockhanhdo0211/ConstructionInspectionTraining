namespace ConstructionInspection.Domain.FormTemplates;

public sealed class FormTemplateVersion
{
    private FormTemplateVersion() { }

    public Guid Id { get; private set; }
    public Guid FormTemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public FormTemplateVersionStatus Status { get; private set; }
    public string SchemaJson { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? PublishedByUserId { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static FormTemplateVersion CreateDraft(
        Guid id,
        Guid formTemplateId,
        int versionNumber,
        string schemaJson,
        Guid actorId,
        DateTime createdAtUtc)
    {
        ValidateIdentity(id, formTemplateId, actorId);
        if (versionNumber <= 0)
            throw Error("form_template.invalid_version_number", "VersionNumber must be positive.");
        EnsureSchemaPresent(schemaJson);

        return new FormTemplateVersion
        {
            Id = id,
            FormTemplateId = formTemplateId,
            VersionNumber = versionNumber,
            Status = FormTemplateVersionStatus.Draft,
            SchemaJson = schemaJson,
            CreatedByUserId = actorId,
            CreatedAt = FormTemplate.EnsureUtc(createdAtUtc)
        };
    }

    public void UpdateDraft(string schemaJson, Guid actorId, DateTime updatedAtUtc)
    {
        EnsureDraft("form_template.version_published", "A Published version is immutable.");
        EnsureActor(actorId);
        EnsureSchemaPresent(schemaJson);
        SchemaJson = schemaJson;
        UpdatedByUserId = actorId;
        UpdatedAt = FormTemplate.EnsureUtc(updatedAtUtc);
    }

    public void Publish(Guid actorId, DateTime publishedAtUtc)
    {
        EnsureDraft("form_template.invalid_transition", "Only a Draft version can be published.");
        EnsureActor(actorId);
        Status = FormTemplateVersionStatus.Published;
        PublishedByUserId = actorId;
        PublishedAt = FormTemplate.EnsureUtc(publishedAtUtc);
    }

    private void EnsureDraft(string code, string message)
    {
        if (Status != FormTemplateVersionStatus.Draft)
            throw Error(code, message);
    }

    private static void ValidateIdentity(Guid id, Guid templateId, Guid actorId)
    {
        if (id == Guid.Empty || templateId == Guid.Empty || actorId == Guid.Empty)
            throw Error("form_template.invalid_identity", "Version, template and actor identifiers are required.");
    }

    private static void EnsureActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
            throw Error("form_template.invalid_actor", "An actor identifier is required.");
    }

    private static void EnsureSchemaPresent(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            throw Error("form_template.schema_invalid", "SchemaJson is required.");
    }

    private static FormTemplateDomainException Error(string code, string message) => new(code, message);
}
