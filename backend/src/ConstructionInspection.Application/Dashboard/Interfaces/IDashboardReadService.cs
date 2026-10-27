using ConstructionInspection.Application.Dashboard.Models;

namespace ConstructionInspection.Application.Dashboard.Interfaces;

public interface IDashboardReadService
{
    Task<DashboardResponseDto> GetAsync(
        DashboardReadScope scope,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
