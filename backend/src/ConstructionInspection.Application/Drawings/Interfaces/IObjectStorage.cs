namespace ConstructionInspection.Application.Drawings.Interfaces;

public interface IObjectStorage
{
    Task UploadAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken);
    Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
    Task<ObjectStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken);
    Task CopyRangeToAsync(string key, Stream destination, long offset, long length, CancellationToken cancellationToken);
}

public sealed record ObjectStorageMetadata(long Length, DateTimeOffset? LastModified);

public sealed class ObjectStorageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
