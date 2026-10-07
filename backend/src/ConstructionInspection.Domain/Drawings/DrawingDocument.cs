namespace ConstructionInspection.Domain.Drawings;

public sealed class DrawingDocument
{
    private DrawingDocument() { }

    public Guid Id { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public int PageCount { get; private set; }
    public DrawingDocumentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static DrawingDocument CreateUploading(
        Guid id,
        string originalFileName,
        string storageKey,
        long fileSize,
        string sha256,
        int pageCount,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty || createdByUserId == Guid.Empty)
            throw Invalid("drawing.invalid_identity", "Drawing and creator identifiers are required.");
        if (string.IsNullOrWhiteSpace(originalFileName)
            || originalFileName.Trim().Length > DrawingDocumentRules.MaximumFileNameLength)
            throw Invalid("drawing.invalid_file_name", "The original file name is invalid.");
        if (string.IsNullOrWhiteSpace(storageKey)
            || storageKey.Length > DrawingDocumentRules.MaximumStorageKeyLength)
            throw Invalid("drawing.invalid_storage_key", "The storage key is invalid.");
        if (fileSize is <= 0 or > DrawingDocumentRules.MaximumFileSize)
            throw Invalid("drawing.invalid_file_size", "The PDF file size is outside the allowed range.");
        if (sha256.Length != DrawingDocumentRules.Sha256HexLength
            || !sha256.All(Uri.IsHexDigit))
            throw Invalid("drawing.invalid_sha256", "The SHA-256 digest is invalid.");
        if (pageCount is <= 0 or > DrawingDocumentRules.MaximumPageCount)
            throw Invalid("drawing.invalid_page_count", "The PDF page count is outside the allowed range.");

        return new DrawingDocument
        {
            Id = id,
            OriginalFileName = NormalizeFileName(originalFileName),
            StorageKey = storageKey,
            ContentType = "application/pdf",
            FileSize = fileSize,
            Sha256 = sha256.ToLowerInvariant(),
            PageCount = pageCount,
            Status = DrawingDocumentStatus.Uploading,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAtUtc.Kind == DateTimeKind.Utc
                ? createdAtUtc
                : createdAtUtc.ToUniversalTime()
        };
    }

    public void MarkReady()
    {
        if (Status != DrawingDocumentStatus.Uploading)
            throw Invalid("drawing.invalid_status_transition", "Only an uploading Drawing can become ready.");
        Status = DrawingDocumentStatus.Ready;
    }

    public void MarkFailed()
    {
        if (Status != DrawingDocumentStatus.Uploading)
            throw Invalid("drawing.invalid_status_transition", "Only an uploading Drawing can fail.");
        Status = DrawingDocumentStatus.Failed;
    }

    public void MarkFailedAfterCompensation()
    {
        if (Status == DrawingDocumentStatus.Failed)
            throw Invalid("drawing.invalid_status_transition", "The Drawing upload is already failed.");
        Status = DrawingDocumentStatus.Failed;
    }

    private static DrawingDocumentDomainException Invalid(string code, string message) => new(code, message);

    private static string NormalizeFileName(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOfAny(['/', '\\']);
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }
}
