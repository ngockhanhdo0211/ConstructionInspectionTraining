using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Domain.FormTemplates;

namespace ConstructionInspection.Application.FormTemplates.Models;

public sealed record FormTemplateListItemDto(
    Guid Id, string Code, string Name, string? Description, FormTemplateStatus Status,
    int? LatestPublishedVersionNumber, int? DraftVersionNumber, string RowVersion);

public sealed record FormTemplateHeaderDto(
    Guid Id, string Code, string Name, string? Description, FormTemplateStatus Status,
    int NextVersionNumber, string RowVersion);

public sealed record FormTemplateVersionMetadataDto(
    Guid Id, int VersionNumber, FormTemplateVersionStatus Status,
    Guid CreatedByUserId, DateTime CreatedAt,
    Guid? UpdatedByUserId, DateTime? UpdatedAt,
    Guid? PublishedByUserId, DateTime? PublishedAt,
    bool IsReferenced, string RowVersion);

public sealed record FormTemplateVersionsDto(
    FormTemplateHeaderDto Template,
    PagedResult<FormTemplateVersionMetadataDto> Versions);

public sealed record FormTemplateVersionDetailDto(
    FormTemplateHeaderDto Template,
    Guid Id, int VersionNumber, FormTemplateVersionStatus Status,
    string SchemaJson, int SchemaUtf8ByteCount,
    Guid CreatedByUserId, DateTime CreatedAt,
    Guid? UpdatedByUserId, DateTime? UpdatedAt,
    Guid? PublishedByUserId, DateTime? PublishedAt,
    string RowVersion);

public sealed record CreatedFormTemplateDto(
    FormTemplateHeaderDto Template,
    FormTemplateVersionDetailDto Version);

public sealed record DeactivatedFormTemplateDto(
    Guid Id, FormTemplateStatus Status,
    Guid UpdatedByUserId, DateTime UpdatedAt,
    Guid DeactivatedByUserId, DateTime DeactivatedAt,
    string RowVersion);

public sealed record FormTemplateQueryCriteria(
    int PageNumber, int PageSize, string? Search, FormTemplateStatus? Status,
    string SortBy, string SortDirection);

public sealed record FormTemplateVersionQueryCriteria(
    int PageNumber, int PageSize, string SortBy, string SortDirection);
