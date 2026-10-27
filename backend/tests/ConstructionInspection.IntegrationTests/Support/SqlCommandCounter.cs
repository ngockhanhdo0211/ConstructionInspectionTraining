using System.Data.Common;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ConstructionInspection.IntegrationTests.Support;

public sealed class SqlCommandCounter(IHttpContextAccessor httpContextAccessor) : DbCommandInterceptor
{
    public const string CorrelationHeader = "X-Dashboard-Benchmark-Id";
    private int count;
    private readonly ConcurrentDictionary<string, int> correlatedCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<SqlCommandSnapshot>> snapshots =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> durations =
        new(StringComparer.Ordinal);

    public int Count => Volatile.Read(ref count);

    public void Reset() => Interlocked.Exchange(ref count, 0);

    public void Reset(string correlationId)
    {
        correlatedCounts.TryRemove(correlationId, out _);
        snapshots.TryRemove(correlationId, out _);
        durations.TryRemove(correlationId, out _);
    }

    public int GetCount(string correlationId) =>
        correlatedCounts.TryGetValue(correlationId, out var value) ? value : 0;

    public IReadOnlyList<SqlCommandSnapshot> GetSnapshots(string correlationId) =>
        snapshots.TryGetValue(correlationId, out var value) ? value.ToArray() : [];

    public IReadOnlyList<double> GetDurations(string correlationId) =>
        durations.TryGetValue(correlationId, out var value) ? value.ToArray() : [];

    private void Increment(DbCommand command)
    {
        Interlocked.Increment(ref count);
        var correlationId = httpContextAccessor.HttpContext?.Request.Headers[CorrelationHeader].ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            correlatedCounts.AddOrUpdate(correlationId, 1, static (_, current) => current + 1);
            var parameters = command.Parameters.Cast<DbParameter>()
                .Select(parameter => new SqlParameterSnapshot(
                    parameter.ParameterName,
                    parameter.Value,
                    parameter.DbType,
                    parameter.Size,
                    parameter is SqlParameter sqlParameter ? sqlParameter.SqlDbType : null))
                .ToArray();
            snapshots.GetOrAdd(correlationId, static _ => new()).Enqueue(
                new SqlCommandSnapshot(command.CommandText, parameters));
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Increment(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Increment(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Increment(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Increment(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Increment(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Increment(command);
        return ValueTask.FromResult(result);
    }


    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    private void RecordDuration(TimeSpan duration)
    {
        var correlationId = httpContextAccessor.HttpContext?.Request.Headers[CorrelationHeader].ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
            durations.GetOrAdd(correlationId, static _ => new()).Enqueue(duration.TotalMilliseconds);
    }
}

public sealed record SqlCommandSnapshot(
    string CommandText,
    IReadOnlyList<SqlParameterSnapshot> Parameters);

public sealed record SqlParameterSnapshot(
    string Name,
    object? Value,
    System.Data.DbType DbType,
    int Size,
    System.Data.SqlDbType? SqlDbType);
