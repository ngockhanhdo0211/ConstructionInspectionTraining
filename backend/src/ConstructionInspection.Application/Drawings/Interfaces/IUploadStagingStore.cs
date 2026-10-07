namespace ConstructionInspection.Application.Drawings.Interfaces;

public interface IUploadStagingStore
{
    Task<StagedUpload> StageAsync(Stream content, long maximumBytes, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string stagingId, CancellationToken cancellationToken);
    Task DeleteAsync(string stagingId);
}

public sealed record StagedUpload(string StagingId, long FileSize, string Sha256);
