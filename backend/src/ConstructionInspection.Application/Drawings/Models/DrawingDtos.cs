using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.Drawings.Models;

public sealed record DrawingContentDescriptor(
    Guid Id,
    string OriginalFileName,
    string StorageKey,
    string ContentType,
    long FileSize,
    string Sha256,
    DrawingDocumentStatus Status,
    DateTime CreatedAt);

public sealed record DrawingListItemDto(Guid Id, string OriginalFileName, long FileSize, int PageCount,
    DrawingDocumentStatus Status, DateTime CreatedAt, Guid CreatedByUserId);

public sealed record DrawingDetailDto(Guid Id, string OriginalFileName, string ContentType, long FileSize,
    string Sha256, int PageCount, DrawingDocumentStatus Status, DateTime CreatedAt, Guid CreatedByUserId,
    string RowVersion);

public sealed record UploadDrawingResponse(DrawingDetailDto Drawing, bool DuplicateWarning);

public sealed record DrawingQueryCriteria(int PageNumber, int PageSize, string? Search, string SortBy, string SortDirection);
