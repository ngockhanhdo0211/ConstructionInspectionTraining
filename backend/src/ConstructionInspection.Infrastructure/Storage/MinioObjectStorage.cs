using ConstructionInspection.Application.Drawings.Interfaces;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace ConstructionInspection.Infrastructure.Storage;

public sealed class MinioObjectStorage(
    IMinioClient client,
    IAmazonS3 s3Client,
    IOptions<ObjectStorageOptions> options) : IObjectStorage
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

    public async Task<ObjectStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var stat = await client.StatObjectAsync(
                new StatObjectArgs().WithBucket(Bucket).WithObject(key), cancellationToken);
            return new ObjectStorageMetadata(stat.Size, stat.LastModified);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (MinioException exception)
        {
            throw new ObjectStorageException(
                "drawing.storage_unavailable", "Object storage is unavailable.", exception);
        }
    }

    public async Task CopyRangeToAsync(
        string key,
        Stream destination,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = Bucket,
                Key = key,
                ByteRange = new ByteRange(offset, offset + length - 1)
            }, cancellationToken);
            if (response.ContentLength != length)
            {
                throw new ObjectStorageException(
                    "drawing.storage_inconsistent", "The Drawing object range does not match its metadata.");
            }
            await response.ResponseStream.CopyToAsync(destination, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ObjectStorageException(
                "drawing.storage_object_missing", "The Drawing object is missing from storage.", exception);
        }
        catch (AmazonS3Exception exception)
        {
            throw new ObjectStorageException(
                "drawing.storage_unavailable", "Object storage is unavailable.", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ObjectStorageException)
        {
            throw new ObjectStorageException(
                "drawing.storage_unavailable", "Object storage is unavailable.", exception);
        }
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
