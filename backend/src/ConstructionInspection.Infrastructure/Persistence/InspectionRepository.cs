using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Domain.Inspections;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class InspectionRepository(ApplicationDbContext context) : IInspectionRepository
{
    public async Task<InspectionObjectForCreate?> GetObjectForCreateAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.ConstructionObjects
            .FromSqlInterpolated($"SELECT * FROM [ConstructionObjects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking()
            .Select(x => new { x.Id, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new(row.Id, row.IsActive);
    }

    public async Task<InspectionTemplateForCreate?> GetTemplateForCreateAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await context.FormTemplates
            .FromSqlInterpolated($"SELECT * FROM [FormTemplates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking()
            .Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (template is null) return null;

        var version = await context.FormTemplateVersions
            .FromSqlInterpolated($"SELECT * FROM [FormTemplateVersions] WITH (UPDLOCK, HOLDLOCK) WHERE [FormTemplateId] = {id} AND [Status] = 'Published'")
            .AsNoTracking()
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new { x.Id, x.VersionNumber, x.SchemaJson })
            .FirstOrDefaultAsync(cancellationToken);
        return new(
            template.Id,
            template.Status == FormTemplateStatus.Active,
            version?.Id,
            version?.VersionNumber,
            version?.SchemaJson);
    }

    public async Task<InspectionWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var inspection = await context.Inspections
            .FromSqlInterpolated($"SELECT * FROM [Inspections] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        if (inspection?.CurrentSubmissionId is null) return null;
        var submission = await context.InspectionSubmissions
            .FromSqlInterpolated($"SELECT * FROM [InspectionSubmissions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {inspection.CurrentSubmissionId.Value}")
            .SingleOrDefaultAsync(cancellationToken);
        var process = await context.BusinessProcesses
            .FromSqlInterpolated($"SELECT * FROM [BusinessProcesses] WITH (UPDLOCK, HOLDLOCK) WHERE [InspectionId] = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        return submission is null || submission.InspectionId != id || process is null
            ? null
            : new(inspection, submission, process);
    }

    public async Task<PinnedTemplateVersion?> GetPinnedVersionAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.FormTemplateVersions.AsNoTracking()
            .Where(x => x.Id == id && x.Status == FormTemplateVersionStatus.Published)
            .Select(x => new { x.Id, x.SchemaJson })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new(row.Id, row.SchemaJson);
    }

    public void AddInspection(Inspection inspection) => context.Inspections.Add(inspection);

    public void AddFoundation(InspectionSubmission submission, BusinessProcess process, ProcessHistory history)
    {
        context.InspectionSubmissions.Add(submission);
        context.BusinessProcesses.Add(process);
        context.ProcessHistories.Add(history);
    }
}
