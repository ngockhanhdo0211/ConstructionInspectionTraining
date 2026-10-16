using System.Text;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Application.FormTemplates.Models;
using ConstructionInspection.Domain.FormTemplates;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class FormTemplateReadService(ApplicationDbContext context) : IFormTemplateReadService
{
    public async Task<PagedResult<FormTemplateListItemDto>> GetTemplatesAsync(
        FormTemplateQueryCriteria criteria,
        CancellationToken cancellationToken)
    {
        IQueryable<FormTemplate> query = context.FormTemplates.AsNoTracking();
        if (criteria.Status.HasValue)
            query = query.Where(item => item.Status == criteria.Status.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(item => item.Code.Contains(search) || item.Name.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        query = ApplyTemplateOrdering(query, criteria.SortBy, criteria.SortDirection);
        var rows = await query
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(item => new TemplateListProjection(
                item.Id,
                item.Code,
                item.Name,
                item.Description,
                item.Status,
                context.FormTemplateVersions
                    .Where(version => version.FormTemplateId == item.Id
                        && version.Status == FormTemplateVersionStatus.Published)
                    .Max(version => (int?)version.VersionNumber),
                context.FormTemplateVersions
                    .Where(version => version.FormTemplateId == item.Id
                        && version.Status == FormTemplateVersionStatus.Draft)
                    .Select(version => (int?)version.VersionNumber)
                    .SingleOrDefault(),
                item.RowVersion))
            .ToArrayAsync(cancellationToken);

        var items = rows.Select(row => new FormTemplateListItemDto(
            row.Id, row.Code, row.Name, row.Description, row.Status,
            row.LatestPublishedVersionNumber, row.DraftVersionNumber,
            Convert.ToBase64String(row.RowVersion))).ToArray();
        return new PagedResult<FormTemplateListItemDto>(
            items,
            criteria.PageNumber,
            criteria.PageSize,
            totalCount,
            TotalPages(totalCount, criteria.PageSize));
    }

    public async Task<FormTemplateVersionsDto?> GetVersionsAsync(
        Guid templateId,
        FormTemplateVersionQueryCriteria criteria,
        CancellationToken cancellationToken)
    {
        var template = await GetTemplateHeaderAsync(templateId, cancellationToken);
        if (template is null) return null;

        var query = context.FormTemplateVersions.AsNoTracking()
            .Where(item => item.FormTemplateId == templateId);
        var totalCount = await query.CountAsync(cancellationToken);
        query = ApplyVersionOrdering(query, criteria.SortBy, criteria.SortDirection);
        var rows = await query
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(item => new VersionProjection(
                item.Id, item.VersionNumber, item.Status,
                item.CreatedByUserId, item.CreatedAt,
                item.UpdatedByUserId, item.UpdatedAt,
                item.PublishedByUserId, item.PublishedAt,
                item.RowVersion))
            .ToArrayAsync(cancellationToken);

        var versions = rows.Select(ToMetadata).ToArray();
        return new FormTemplateVersionsDto(
            template,
            new PagedResult<FormTemplateVersionMetadataDto>(
                versions,
                criteria.PageNumber,
                criteria.PageSize,
                totalCount,
                TotalPages(totalCount, criteria.PageSize)));
    }

    public async Task<FormTemplateVersionDetailDto?> GetVersionDetailAsync(
        Guid templateId,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        var template = await GetTemplateHeaderAsync(templateId, cancellationToken);
        if (template is null) return null;

        var row = await context.FormTemplateVersions.AsNoTracking()
            .Where(item => item.FormTemplateId == templateId && item.VersionNumber == versionNumber)
            .Select(item => new VersionDetailProjection(
                item.Id, item.VersionNumber, item.Status, item.SchemaJson,
                item.CreatedByUserId, item.CreatedAt,
                item.UpdatedByUserId, item.UpdatedAt,
                item.PublishedByUserId, item.PublishedAt,
                item.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        return new FormTemplateVersionDetailDto(
            template,
            row.Id,
            row.VersionNumber,
            row.Status,
            row.SchemaJson,
            Encoding.UTF8.GetByteCount(row.SchemaJson),
            row.CreatedByUserId,
            row.CreatedAt,
            row.UpdatedByUserId,
            row.UpdatedAt,
            row.PublishedByUserId,
            row.PublishedAt,
            Convert.ToBase64String(row.RowVersion));
    }

    private async Task<FormTemplateHeaderDto?> GetTemplateHeaderAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await context.FormTemplates.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TemplateHeaderProjection(
                item.Id, item.Code, item.Name, item.Description, item.Status,
                item.NextVersionNumber, item.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new FormTemplateHeaderDto(
                row.Id, row.Code, row.Name, row.Description, row.Status,
                row.NextVersionNumber, Convert.ToBase64String(row.RowVersion));
    }

    private static FormTemplateVersionMetadataDto ToMetadata(VersionProjection row) => new(
        row.Id,
        row.VersionNumber,
        row.Status,
        row.CreatedByUserId,
        row.CreatedAt,
        row.UpdatedByUserId,
        row.UpdatedAt,
        row.PublishedByUserId,
        row.PublishedAt,
        false,
        Convert.ToBase64String(row.RowVersion));

    private static IQueryable<FormTemplate> ApplyTemplateOrdering(
        IQueryable<FormTemplate> query,
        string sortBy,
        string direction)
    {
        var descending = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "code" => descending ? query.OrderByDescending(x => x.Code).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Code).ThenBy(x => x.Id),
            "createdat" => descending ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "updatedat" => descending ? query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
            _ => descending ? query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id)
        };
    }

    private static IQueryable<FormTemplateVersion> ApplyVersionOrdering(
        IQueryable<FormTemplateVersion> query,
        string sortBy,
        string direction)
    {
        var descending = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "createdat" => descending ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "publishedat" => descending ? query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id) : query.OrderBy(x => x.PublishedAt).ThenBy(x => x.Id),
            _ => descending ? query.OrderByDescending(x => x.VersionNumber).ThenByDescending(x => x.Id) : query.OrderBy(x => x.VersionNumber).ThenBy(x => x.Id)
        };
    }

    private static int TotalPages(int totalCount, int pageSize) =>
        totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private sealed record TemplateListProjection(
        Guid Id, string Code, string Name, string? Description, FormTemplateStatus Status,
        int? LatestPublishedVersionNumber, int? DraftVersionNumber, byte[] RowVersion);
    private sealed record TemplateHeaderProjection(
        Guid Id, string Code, string Name, string? Description, FormTemplateStatus Status,
        int NextVersionNumber, byte[] RowVersion);
    private record VersionProjection(
        Guid Id, int VersionNumber, FormTemplateVersionStatus Status,
        Guid CreatedByUserId, DateTime CreatedAt,
        Guid? UpdatedByUserId, DateTime? UpdatedAt,
        Guid? PublishedByUserId, DateTime? PublishedAt,
        byte[] RowVersion);
    private sealed record VersionDetailProjection(
        Guid Id, int VersionNumber, FormTemplateVersionStatus Status, string SchemaJson,
        Guid CreatedByUserId, DateTime CreatedAt,
        Guid? UpdatedByUserId, DateTime? UpdatedAt,
        Guid? PublishedByUserId, DateTime? PublishedAt,
        byte[] RowVersion);
}
