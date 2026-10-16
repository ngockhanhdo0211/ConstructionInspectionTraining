using ConstructionInspection.Domain.FormTemplates;

namespace ConstructionInspection.Application.FormTemplates.Interfaces;

public interface IFormTemplateRepository
{
    Task<FormTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<FormTemplateVersion?> GetVersionForUpdateAsync(Guid templateId, int versionNumber, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);
    Task<bool> HasDraftAsync(Guid templateId, CancellationToken cancellationToken);
    void Add(FormTemplate template);
    void Add(FormTemplateVersion version);
}
