namespace ConstructionInspection.Domain.Inspections;

public sealed class Inspection
{
    private Inspection() { }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid ConstructionObjectId { get; private set; }
    public Guid FormTemplateVersionId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ReviewerUserId { get; private set; }
    public Guid? CurrentSubmissionId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Inspection Create(
        Guid id,
        string number,
        Guid constructionObjectId,
        Guid formTemplateVersionId,
        string title,
        string? description,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty || constructionObjectId == Guid.Empty
            || formTemplateVersionId == Guid.Empty || createdByUserId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_identity", "Inspection, object, template version and actor identifiers are required.");
        if (string.IsNullOrWhiteSpace(number) || number.Length > InspectionRules.MaximumNumberLength)
            throw InspectionRules.Error("inspection.invalid_number", "Inspection Number is required and exceeds the allowed length.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > InspectionRules.MaximumTitleLength)
            throw InspectionRules.Error("inspection.invalid_title", "Title is required and exceeds the allowed length.");
        if (description?.Trim().Length > InspectionRules.MaximumDescriptionLength)
            throw InspectionRules.Error("inspection.invalid_description", "Description exceeds the allowed length.");

        return new Inspection
        {
            Id = id,
            Number = number,
            ConstructionObjectId = constructionObjectId,
            FormTemplateVersionId = formTemplateVersionId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAt = InspectionRules.EnsureUtc(createdAtUtc)
        };
    }

    public void SetCurrentSubmission(Guid submissionId)
    {
        if (submissionId == Guid.Empty)
            throw InspectionRules.Error("inspection.invalid_submission", "Current submission is required.");
        CurrentSubmissionId = submissionId;
    }

    public void MarkDraftUpdated(Guid actorId, DateTime updatedAtUtc)
    {
        if (actorId != CreatedByUserId)
            throw InspectionRules.Error("inspection.not_owner", "Only the creator can update the Inspection Draft.");
        UpdatedAt = InspectionRules.EnsureUtc(updatedAtUtc);
    }
}
