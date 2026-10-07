namespace ConstructionInspection.Application.Drawings.Interfaces;

public interface IObjectStorage
{
    Task UploadAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken);
    Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}
