using ConstructionInspection.Application.Checklists.Models;
using ConstructionInspection.Application.Common.Models;

namespace ConstructionInspection.Application.Checklists.Interfaces;

public interface IChecklistReadService
{
    Task<PagedResult<ChecklistListItemDto>> GetPageAsync(ChecklistQueryCriteria criteria, CancellationToken cancellationToken);
    Task<ChecklistDetailDto?> GetDetailAsync(Guid id, ChecklistReadScope scope, CancellationToken cancellationToken);
    Task<PagedResult<ChecklistHistoryDto>?> GetHistoryAsync(Guid id, int pageNumber, int pageSize,
        ChecklistReadScope scope, CancellationToken cancellationToken);
    Task<PagedResult<ChecklistSubmissionListItemDto>?> GetSubmissionsAsync(Guid id, int pageNumber, int pageSize,
        ChecklistReadScope scope, CancellationToken cancellationToken);
    Task<ChecklistSubmissionDetailDto?> GetSubmissionAsync(Guid id, int submissionNumber,
        ChecklistReadScope scope, CancellationToken cancellationToken);
}
