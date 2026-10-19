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

public sealed record ReviewerOptionDto(Guid UserId, string DisplayName);

public sealed record InspectionWorkflowResultDto(
    Guid InspectionId, InspectionStatus Status, Guid? ReviewerUserId,
    int CurrentRevisionNumber, DateTime? CompletedAt,
    string InspectionRowVersion, string SubmissionRowVersion, string ProcessRowVersion);

public sealed record ProcessHistoryDto(
    Guid Id, string Action, InspectionStatus? FromStatus, InspectionStatus ToStatus,
    Guid ActorUserId, string ActorDisplayName,
    Guid? PreviousReviewerUserId, string? PreviousReviewerDisplayName,
    Guid? NewReviewerUserId, string? NewReviewerDisplayName,
    string? Reason, DateTime CreatedAt, Guid? SubmissionId);

public sealed record InspectionRevisionListItemDto(
    Guid Id, int RevisionNumber, Guid CreatedByUserId, string CreatedByDisplayName,
    DateTime CreatedAt, DateTime? UpdatedAt, DateTime? SubmittedAt,
    bool IsImmutable, bool IsCurrent, string RowVersion);

public sealed record InspectionRevisionDetailDto(
    Guid InspectionId, Guid Id, int RevisionNumber, string DataJson,
    Guid CreatedByUserId, string CreatedByDisplayName,
    DateTime CreatedAt, DateTime? UpdatedAt, DateTime? SubmittedAt,
    bool IsImmutable, bool IsCurrent, string RowVersion,
    InspectionTemplateDto Template);
