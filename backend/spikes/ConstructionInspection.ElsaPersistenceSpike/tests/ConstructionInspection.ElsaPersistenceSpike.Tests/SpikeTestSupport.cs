using Microsoft.Data.SqlClient;
using Xunit;

namespace ConstructionInspection.ElsaPersistenceSpike.Tests;

internal static class SpikeTestSupport
{
    public static string ConnectionString => SpikeDatabaseSafety.RequireSafeConnectionString(
        Environment.GetEnvironmentVariable("ConnectionStrings__ElsaSpikeConnection"));

    public static InspectionEventPayload Event(
        Guid inspectionId,
        string eventType,
        Guid? domainEventId = null,
        Guid? actorUserId = null,
        int revision = 1)
    {
        var (from, to) = eventType switch
        {
            InspectionEventTypes.Created => ((string?)null, ShadowStates.Draft),
            InspectionEventTypes.Submitted => (ShadowStates.Draft, ShadowStates.Submitted),
            InspectionEventTypes.StartReview => (ShadowStates.Submitted, ShadowStates.UnderReview),
            InspectionEventTypes.Approved => (ShadowStates.UnderReview, ShadowStates.Approved),
            InspectionEventTypes.Rejected => (ShadowStates.UnderReview, ShadowStates.Rejected),
            InspectionEventTypes.RecheckRequested => (ShadowStates.UnderReview, ShadowStates.Recheck),
            InspectionEventTypes.RevisionCreated => (ShadowStates.Recheck, ShadowStates.Recheck),
            InspectionEventTypes.Resubmitted => (ShadowStates.Recheck, ShadowStates.Submitted),
            InspectionEventTypes.ReviewerReassigned => (ShadowStates.UnderReview, ShadowStates.Submitted),
            _ => (null, string.Empty)
        };

        return new InspectionEventPayload(
            1,
            domainEventId ?? Guid.NewGuid(),
            eventType,
            inspectionId,
            Guid.NewGuid(),
            actorUserId ?? Guid.NewGuid(),
            from,
            to,
            Guid.NewGuid(),
            revision,
            null,
            null);
    }

    public static async Task<long> DatabaseSizeBytesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(SpikeDatabaseSafety.ExpectedDatabaseName, connection.Database);
        await using var command = new SqlCommand(
            "SELECT SUM(CAST(size AS bigint)) * 8192 FROM sys.database_files;",
            connection);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
