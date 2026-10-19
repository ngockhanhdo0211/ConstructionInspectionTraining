using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Models;

namespace ConstructionInspection.Application.Inspections.Interfaces;

public sealed record ReviewerEligibility(Guid UserId, string DisplayName, bool IsActive, bool HasReviewerRole);
public sealed record ReviewerOptionCriteria(int PageNumber, int PageSize, string? Search);

public interface IInspectionReviewerService
{
    Task<ReviewerEligibility?> GetForAssignmentAsync(Guid userId, CancellationToken cancellationToken);
    Task<PagedResult<ReviewerOptionDto>> GetOptionsAsync(
        ReviewerOptionCriteria criteria, CancellationToken cancellationToken);
}
