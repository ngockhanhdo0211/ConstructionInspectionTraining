using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using ConstructionInspection.Domain.Drawings;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class DrawingDocumentReadService(ApplicationDbContext context) : IDrawingDocumentReadService
{
    public async Task<PagedResult<DrawingListItemDto>> GetPageAsync(DrawingQueryCriteria c, CancellationToken ct)
    {
        IQueryable<DrawingDocument> query = context.DrawingDocuments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(c.Search))
        {
            var search = c.Search.Trim();
            query = query.Where(x => x.OriginalFileName.Contains(search));
        }
        var total = await query.CountAsync(ct);
        var desc = c.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<DrawingDocument> ordered = c.SortBy.ToLowerInvariant() switch
        {
            "filename" => desc ? query.OrderByDescending(x => x.OriginalFileName) : query.OrderBy(x => x.OriginalFileName),
            "filesize" => desc ? query.OrderByDescending(x => x.FileSize) : query.OrderBy(x => x.FileSize),
            "pagecount" => desc ? query.OrderByDescending(x => x.PageCount) : query.OrderBy(x => x.PageCount),
            "status" => desc ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            _ => desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
        };
        var items = await ordered.ThenBy(x => x.Id).Skip((c.PageNumber - 1) * c.PageSize).Take(c.PageSize)
            .Select(x => new DrawingListItemDto(x.Id, x.OriginalFileName, x.FileSize, x.PageCount,
                x.Status, x.CreatedAt, x.CreatedByUserId)).ToArrayAsync(ct);
        return new(items, c.PageNumber, c.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)c.PageSize));
    }

    public async Task<DrawingDetailDto?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var x = await context.DrawingDocuments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new Projection(x.Id, x.OriginalFileName, x.ContentType, x.FileSize, x.Sha256,
                x.PageCount, x.Status, x.CreatedAt, x.CreatedByUserId, x.RowVersion)).SingleOrDefaultAsync(ct);
        return x is null ? null : new(x.Id, x.OriginalFileName, x.ContentType, x.FileSize, x.Sha256,
            x.PageCount, x.Status, x.CreatedAt, x.CreatedByUserId, Convert.ToBase64String(x.RowVersion));
    }

    public Task<DrawingContentDescriptor?> GetContentDescriptorAsync(Guid id, CancellationToken ct) =>
        context.DrawingDocuments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new DrawingContentDescriptor(x.Id, x.OriginalFileName, x.StorageKey, x.ContentType,
                x.FileSize, x.Sha256, x.Status, x.CreatedAt))
            .SingleOrDefaultAsync(ct);

    private sealed record Projection(Guid Id, string OriginalFileName, string ContentType, long FileSize,
        string Sha256, int PageCount, DrawingDocumentStatus Status, DateTime CreatedAt, Guid CreatedByUserId, byte[] RowVersion);
}
