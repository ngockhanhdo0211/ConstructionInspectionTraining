using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Domain.FormTemplates;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class FormTemplateRepository(ApplicationDbContext context) : IFormTemplateRepository
{
    public Task<FormTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.FormTemplates
            .FromSqlInterpolated($"SELECT * FROM [FormTemplates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(cancellationToken);

    public Task<FormTemplateVersion?> GetVersionForUpdateAsync(
        Guid templateId,
        int versionNumber,
        CancellationToken cancellationToken) => context.FormTemplateVersions
            .FromSqlInterpolated($"SELECT * FROM [FormTemplateVersions] WITH (UPDLOCK, HOLDLOCK) WHERE [FormTemplateId] = {templateId} AND [VersionNumber] = {versionNumber}")
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.FormTemplates.AnyAsync(item => item.Code == normalizedCode, cancellationToken);

    public Task<bool> HasDraftAsync(Guid templateId, CancellationToken cancellationToken) =>
        context.FormTemplateVersions.AnyAsync(
            item => item.FormTemplateId == templateId && item.Status == FormTemplateVersionStatus.Draft,
            cancellationToken);

    public void Add(FormTemplate template) => context.FormTemplates.Add(template);

    public void Add(FormTemplateVersion version) => context.FormTemplateVersions.Add(version);
}
