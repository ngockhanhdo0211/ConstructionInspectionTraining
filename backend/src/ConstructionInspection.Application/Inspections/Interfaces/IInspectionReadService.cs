using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.Inspections.Interfaces;

public sealed record InspectionQueryCriteria(
    int PageNumber, int PageSize, string? Search, InspectionStatus? Status,
    Guid? ConstructionObjectId, Guid? ReviewerUserId, string SortBy, string SortDirection,
    InspectionReadScope Scope);

public sealed record FormTemplateOptionCriteria(int PageNumber, int PageSize, string? Search);
public sealed record InspectionHistoryCriteria(Guid InspectionId, int PageNumber, int PageSize, InspectionReadScope Scope);
public sealed record InspectionRevisionCriteria(Guid InspectionId, int PageNumber, int PageSize, InspectionReadScope Scope);

public interface IInspectionReadService
{
    Task<PagedResult<InspectionListItemDto>> GetPageAsync(InspectionQueryCriteria criteria, CancellationToken cancellationToken);
    Task<InspectionDetailDto?> GetDetailAsync(Guid id, InspectionReadScope scope, CancellationToken cancellationToken);
    Task<PagedResult<FormTemplateOptionDto>> GetFormTemplateOptionsAsync(
        FormTemplateOptionCriteria criteria, CancellationToken cancellationToken);
    Task<PagedResult<ProcessHistoryDto>?> GetHistoryAsync(
        InspectionHistoryCriteria criteria, CancellationToken cancellationToken);
    Task<PagedResult<InspectionRevisionListItemDto>?> GetRevisionsAsync(
        InspectionRevisionCriteria criteria, CancellationToken cancellationToken);
    Task<InspectionRevisionDetailDto?> GetRevisionDetailAsync(
        Guid inspectionId, int revisionNumber, InspectionReadScope scope, CancellationToken cancellationToken);
}
