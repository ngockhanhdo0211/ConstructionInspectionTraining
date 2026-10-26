using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Rfis;

namespace ConstructionInspection.Application.Rfis.Interfaces;

public sealed record RfiObjectState(Guid Id, bool IsActive);
public sealed record RfiWriteAggregate(Rfi Rfi, BusinessProcess Process, List<RfiEntry> Entries);
public sealed record RfiReviewerEligibility(Guid UserId, string DisplayName, bool IsActive, bool HasReviewerRole);

public interface IRfiRepository
{
    Task<RfiObjectState?> GetObjectForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<RfiWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    void AddRfi(Rfi rfi);
    void AddEntry(RfiEntry entry);
    void AddFoundation(BusinessProcess process, ProcessHistory history);
    void AddHistory(ProcessHistory history);
}

public interface IRfiReadService
{
    Task<PagedResult<RfiListItemDto>> GetPageAsync(RfiQueryCriteria criteria, CancellationToken cancellationToken);
    Task<RfiDetailDto?> GetDetailAsync(Guid id, RfiReadScope scope, CancellationToken cancellationToken);
    Task<PagedResult<RfiHistoryDto>?> GetHistoryAsync(Guid id, int pageNumber, int pageSize,
        RfiReadScope scope, CancellationToken cancellationToken);
}

public interface IRfiReviewerService
{
    Task<RfiReviewerEligibility?> GetForAssignmentAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<RfiResponderOptionDto>> GetOptionsAsync(int pageNumber, int pageSize,
        string? search, CancellationToken cancellationToken);
}

public interface IRfiNumberSequence
{
    Task<long> NextAsync(CancellationToken cancellationToken);
}
