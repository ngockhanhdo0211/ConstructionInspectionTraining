using System.Buffers;
using System.Security.Cryptography;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Drawings.Interfaces;

namespace ConstructionInspection.Infrastructure.Storage;

public sealed class TemporaryFileStagingStore : IUploadStagingStore
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ConstructionInspectionTraining", "uploads");

    public async Task<StagedUpload> StageAsync(Stream content, long maximumBytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        var id = $"{Guid.NewGuid():N}.tmp";
        var path = Resolve(id);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long length = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                length += read;
                if (length > maximumBytes) throw new PayloadTooLargeException("The uploaded PDF exceeds 50 MiB.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (length == 0) throw new FluentValidation.ValidationException("The uploaded file is empty.");
            return new StagedUpload(id, length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task<Stream> OpenReadAsync(string stagingId, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(Resolve(stagingId), FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));

    public Task DeleteAsync(string stagingId)
    {
        var path = Resolve(stagingId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private static string Resolve(string id)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(id) != id)
            throw new InvalidOperationException("Invalid staging identifier.");
        return Path.Combine(Root, id);
    }
}
