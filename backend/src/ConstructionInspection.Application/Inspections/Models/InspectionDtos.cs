using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.Inspections.Models;

public sealed record InspectionListItemDto(
    Guid Id, string Number, string Title, Guid ConstructionObjectId,
    Guid FormTemplateVersionId, int FormTemplateVersionNumber,
    InspectionStatus Status, Guid CreatedByUserId, Guid? ReviewerUserId,
    DateTime CreatedAt, DateTime? UpdatedAt, string RowVersion);

public sealed record InspectionSubmissionDto(
    Guid Id, int RevisionNumber, string DataJson, DateTime CreatedAt,
    DateTime? UpdatedAt, DateTime? SubmittedAt, string RowVersion);

public sealed record InspectionTemplateDto(
    Guid Id, string Code, string Name, Guid VersionId, int VersionNumber, string SchemaJson);

public sealed record InspectionDetailDto(
    Guid Id, string Number, Guid ConstructionObjectId, string ConstructionObjectCode,
    string ConstructionObjectName, string Title, string? Description,
    InspectionTemplateDto Template, InspectionStatus Status,
    Guid CreatedByUserId, Guid? ReviewerUserId, DateTime CreatedAt, DateTime? UpdatedAt,
    InspectionSubmissionDto CurrentRevision, string RowVersion, string ProcessRowVersion);

public sealed record InspectionDraftDto(
    Guid Id, int RevisionNumber, string DataJson, DateTime UpdatedAt,
    string InspectionRowVersion, string SubmissionRowVersion, string ProcessRowVersion);

public sealed record FormTemplateOptionDto(
    Guid FormTemplateId, string Code, string Name, int LatestPublishedVersionNumber);
