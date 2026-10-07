using ConstructionInspection.Application.Drawings.Interfaces;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace ConstructionInspection.Infrastructure.Storage;

public sealed class MinioObjectStorage(IMinioClient client, IOptions<ObjectStorageOptions> options) : IObjectStorage
{
    private string Bucket => options.Value.BucketName;

    public async Task UploadAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        await client.PutObjectAsync(new PutObjectArgs().WithBucket(Bucket).WithObject(key)
            .WithStreamData(content).WithObjectSize(length).WithContentType(contentType), cancellationToken);
    }

    public async Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken)
    {
        await client.GetObjectAsync(new GetObjectArgs().WithBucket(Bucket).WithObject(key)
            .WithCallbackStream(async (source, token) => await source.CopyToAsync(destination, token)), cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(Bucket).WithObject(key), cancellationToken);

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await client.StatObjectAsync(new StatObjectArgs().WithBucket(Bucket).WithObject(key), cancellationToken);
            return true;
        }
        catch (ObjectNotFoundException) { return false; }
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(Bucket), cancellationToken);
        if (!exists) await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(Bucket), cancellationToken);
    }
}
