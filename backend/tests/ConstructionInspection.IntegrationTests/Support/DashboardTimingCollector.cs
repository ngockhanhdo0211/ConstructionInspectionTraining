using System.Collections.Concurrent;
using System.Diagnostics;
using ConstructionInspection.Application.Dashboard.Interfaces;
using ConstructionInspection.Application.Dashboard.Models;
using Microsoft.AspNetCore.Http;

namespace ConstructionInspection.IntegrationTests.Support;

public sealed class DashboardTimingCollector
{
    private readonly ConcurrentDictionary<string, double> readServiceMilliseconds =
        new(StringComparer.Ordinal);

    public void Reset(string correlationId) => readServiceMilliseconds.TryRemove(correlationId, out _);

    public void Record(string correlationId, double elapsedMilliseconds) =>
        readServiceMilliseconds[correlationId] = elapsedMilliseconds;

    public double GetReadServiceMilliseconds(string correlationId) =>
        readServiceMilliseconds.TryGetValue(correlationId, out var value) ? value : 0;
}

public sealed class TimedDashboardReadService(
    IDashboardReadService inner,
    IHttpContextAccessor httpContextAccessor,
    DashboardTimingCollector collector) : IDashboardReadService
{
    public async Task<DashboardResponseDto> GetAsync(
        DashboardReadScope scope,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return await inner.GetAsync(scope, nowUtc, cancellationToken);
        }
        finally
        {
            watch.Stop();
            var correlationId = httpContextAccessor.HttpContext?.Request
                .Headers[SqlCommandCounter.CorrelationHeader].ToString();
            if (!string.IsNullOrWhiteSpace(correlationId))
                collector.Record(correlationId, watch.Elapsed.TotalMilliseconds);
        }
    }
}
