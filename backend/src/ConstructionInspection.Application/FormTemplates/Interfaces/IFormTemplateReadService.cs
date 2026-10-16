using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.FormTemplates.Models;

namespace ConstructionInspection.Application.FormTemplates.Interfaces;

public interface IFormTemplateReadService
{
    Task<PagedResult<FormTemplateListItemDto>> GetTemplatesAsync(FormTemplateQueryCriteria criteria, CancellationToken cancellationToken);
    Task<FormTemplateVersionsDto?> GetVersionsAsync(Guid templateId, FormTemplateVersionQueryCriteria criteria, CancellationToken cancellationToken);
    Task<FormTemplateVersionDetailDto?> GetVersionDetailAsync(Guid templateId, int versionNumber, CancellationToken cancellationToken);
}
