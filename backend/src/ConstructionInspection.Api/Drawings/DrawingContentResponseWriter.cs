using System.Globalization;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using Microsoft.Net.Http.Headers;

namespace ConstructionInspection.Api.Drawings;

public sealed class DrawingContentResponseWriter(
    IObjectStorage storage,
    ILogger<DrawingContentResponseWriter> logger) : IDrawingContentResponseWriter
{
    public async Task WriteAsync(
        HttpContext context,
        DrawingContentDescriptor drawing,
        CancellationToken cancellationToken)
    {
        var metadata = await storage.GetMetadataAsync(drawing.StorageKey, cancellationToken)
            ?? throw new ObjectStorageException(
                "drawing.storage_object_missing", "The Drawing object is missing from storage.");
        if (metadata.Length != drawing.FileSize)
            throw new ObjectStorageException(
                "drawing.storage_inconsistent", "The Drawing object does not match its metadata.");

        var response = context.Response;
        var request = context.Request;
        var etag = $"\"{drawing.Sha256}\"";
        var lastModified = metadata.LastModified ?? new DateTimeOffset(DateTime.SpecifyKind(drawing.CreatedAt, DateTimeKind.Utc));
        lastModified = lastModified.AddTicks(-(lastModified.Ticks % TimeSpan.TicksPerSecond));

        SetCommonHeaders(response, drawing, etag, lastModified);

        if (MatchesIfNoneMatch(request.Headers.IfNoneMatch.ToString(), etag)
            || (request.Headers.IfNoneMatch.Count == 0
                && MatchesIfModifiedSince(request.Headers.IfModifiedSince.ToString(), lastModified)))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        var requestedRange = ParseRange(request.Headers.Range.ToString(), drawing.FileSize);
        if (requestedRange is not null && !MatchesIfRange(request.Headers.IfRange.ToString(), etag, lastModified))
            requestedRange = null;

        var range = requestedRange ?? new ByteRange(0, drawing.FileSize - 1);
        if (requestedRange is not null)
        {
            response.StatusCode = StatusCodes.Status206PartialContent;
            response.Headers.ContentRange = $"bytes {range.Start}-{range.End}/{drawing.FileSize}";
        }
        else
        {
            response.StatusCode = StatusCodes.Status200OK;
        }

        response.ContentLength = range.Length;
        try
        {
            await storage.CopyRangeToAsync(
                drawing.StorageKey, response.Body, range.Start, range.Length, cancellationToken);
        }
        catch (ObjectStorageException) when (!response.HasStarted)
        {
            response.Clear();
            throw;
        }
        catch (ObjectStorageException exception)
        {
            logger.LogWarning(
                "Drawing content stream failed after response start. DrawingId={DrawingId}, Code={Code}, PlannedBytes={PlannedBytes}, TraceId={TraceId}.",
                drawing.Id, exception.Code, range.Length, context.TraceIdentifier);
            context.Abort();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Abort();
        }
    }

    private static void SetCommonHeaders(
        HttpResponse response,
        DrawingContentDescriptor drawing,
        string etag,
        DateTimeOffset lastModified)
    {
        response.ContentType = "application/pdf";
        response.Headers.AcceptRanges = "bytes";
        response.Headers.ETag = etag;
        response.Headers.LastModified = lastModified.ToString("R", CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "private, max-age=0, must-revalidate";
        response.Headers.XContentTypeOptions = "nosniff";
        var disposition = new ContentDispositionHeaderValue("inline")
        {
            FileNameStar = drawing.OriginalFileName
        };
        response.Headers.ContentDisposition = disposition.ToString();
    }

    private static bool MatchesIfNoneMatch(string values, string etag) =>
        values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value == "*" || string.Equals(RemoveWeakPrefix(value), etag, StringComparison.Ordinal));

    private static string RemoveWeakPrefix(string value) =>
        value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;

    private static bool MatchesIfModifiedSince(string value, DateTimeOffset lastModified) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
        && lastModified <= parsed.ToUniversalTime();

    private static bool MatchesIfRange(string value, string etag, DateTimeOffset lastModified)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"') || trimmed.StartsWith("W/", StringComparison.Ordinal))
            return string.Equals(trimmed, etag, StringComparison.Ordinal);
        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            && lastModified <= date.ToUniversalTime();
    }

    private static ByteRange? ParseRange(string value, long length)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!RangeHeaderValue.TryParse(value, out var parsed)
            || parsed is null
            || !string.Equals(parsed.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase))
            throw new RangeNotSatisfiableException(length);
        if (parsed.Ranges.Count != 1)
            throw new RangeNotSatisfiableException(length, parsed.Ranges.Count > 1
                ? "drawing.multiple_ranges_not_supported"
                : "drawing.range_not_satisfiable");

        var requested = parsed.Ranges.Single();
        if (!requested.From.HasValue)
        {
            var suffix = requested.To;
            if (!suffix.HasValue || suffix.Value <= 0)
                throw new RangeNotSatisfiableException(length);
            var start = Math.Max(0, length - suffix.Value);
            return new ByteRange(start, length - 1);
        }

        var startValue = requested.From.Value;
        if (startValue < 0 || startValue >= length)
            throw new RangeNotSatisfiableException(length);
        if (!requested.To.HasValue) return new ByteRange(startValue, length - 1);
        var endValue = requested.To.Value;
        if (endValue < startValue)
            throw new RangeNotSatisfiableException(length);
        return new ByteRange(startValue, Math.Min(endValue, length - 1));
    }

    private sealed record ByteRange(long Start, long End)
    {
        public long Length => End - Start + 1;
    }
}

public sealed class RangeNotSatisfiableException(long resourceLength, string code = "drawing.range_not_satisfiable")
    : Exception("The requested byte range cannot be satisfied.")
{
    public long ResourceLength { get; } = resourceLength;
    public string Code { get; } = code;
}
