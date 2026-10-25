using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Extensions;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConstructionInspection.ElsaPersistenceSpike;

public sealed record ShadowSnapshot(
    string WorkflowInstanceId,
    string RuntimeSubStatus,
    string ShadowState,
    Guid? LastDomainEventId,
    IReadOnlyCollection<string> BookmarkEvents);

public sealed class PersistenceSpikeHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _connectionString;
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IStimulusSender _stimulusSender;
    private readonly IDbContextFactory<SpikeDbContext> _spikeDbFactory;
    private readonly IDbContextFactory<ManagementElsaDbContext> _managementDbFactory;

    private PersistenceSpikeHost(IHost host, string connectionString)
    {
        _host = host;
        _connectionString = connectionString;
        _workflowRuntime = host.Services.GetRequiredService<IWorkflowRuntime>();
        _stimulusSender = host.Services.GetRequiredService<IStimulusSender>();
        _spikeDbFactory = host.Services.GetRequiredService<IDbContextFactory<SpikeDbContext>>();
        _managementDbFactory = host.Services.GetRequiredService<IDbContextFactory<ManagementElsaDbContext>>();
    }

    public static async Task<PersistenceSpikeHost> CreateAsync(
        string connectionString,
        bool runMigrations = false,
        CancellationToken cancellationToken = default)
    {
        connectionString = SpikeDatabaseSafety.RequireSafeConnectionString(connectionString);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContextFactory<SpikeDbContext>(options => options.UseSqlServer(connectionString));
        builder.Services.AddElsa(elsa =>
        {
            elsa.UseWorkflowManagement(management => management.UseEntityFrameworkCore(ef =>
            {
                ef.UseSqlServer(connectionString);
                ef.RunMigrations = runMigrations;
            }));
            elsa.UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore(ef =>
            {
                ef.UseSqlServer(connectionString);
                ef.RunMigrations = runMigrations;
                ef.UseWorkflowCommitTransaction = true;
            }));
            elsa.AddActivity<WaitForPersistentInspectionLifecycle>();
            elsa.AddWorkflow<PersistentInspectionWorkflow>();
            elsa.AddActivity<WaitForWorkflowOwnedInspectionEvent>();
            elsa.AddWorkflow<WorkflowOwnedReceiptWorkflow>();
        });

        var host = builder.Build();
        try
        {
            await host.StartAsync(cancellationToken);
            if (runMigrations)
                await SpikeSchemaInitializer.InitializeAsync(connectionString, cancellationToken);
            return new PersistenceSpikeHost(host, connectionString);
        }
        catch
        {
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
            throw;
        }
    }

    public async Task<DispatchResult> DispatchAsync(
        InspectionEventPayload payload,
        CrashPoint crashPoint = CrashPoint.None,
        CancellationToken cancellationToken = default)
    {
        ValidatePayload(payload);
        await using var eventLock = await SqlApplicationLock.AcquireAsync(
            _connectionString,
            $"ElsaSpike:{payload.DomainEventId:D}",
            cancellationToken);

        await using var db = await _spikeDbFactory.CreateDbContextAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload, SpikeJson.Options);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
        var inbox = await db.InboxMessages.SingleOrDefaultAsync(
            x => x.DomainEventId == payload.DomainEventId,
            cancellationToken);

        if (inbox is not null)
        {
            if (!string.Equals(inbox.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                inbox.Outcome = "Rejected";
                inbox.ErrorCode = "PayloadConflict";
                await db.SaveChangesAsync(cancellationToken);
                return DispatchResult.PayloadConflict;
            }
            if (inbox.ProcessedAtUtc.HasValue)
                return DispatchResult.Duplicate;
            if (await IsReflectedAsync(payload, cancellationToken))
            {
                await MarkProcessedAsync(db, inbox, payload, cancellationToken);
                return DispatchResult.Reconciled;
            }
        }
        else
        {
            inbox = new SpikeInboxMessage
            {
                Id = Guid.NewGuid(),
                DomainEventId = payload.DomainEventId,
                InspectionId = payload.InspectionId,
                EventType = payload.EventType,
                PayloadJson = payloadJson,
                PayloadHash = payloadHash,
                ReceivedAtUtc = DateTime.UtcNow,
                Outcome = "Pending"
            };
            db.InboxMessages.Add(inbox);
            if (crashPoint == CrashPoint.BeforeInboxCommit)
                throw new SimulatedCrashException(crashPoint);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (crashPoint == CrashPoint.AfterInboxCommitBeforeElsa)
            throw new SimulatedCrashException(crashPoint);

        var result = await ApplyToElsaAsync(payload, cancellationToken);
        if (result != DispatchResult.Processed)
        {
            inbox.Outcome = "Rejected";
            inbox.ErrorCode = result.ToString();
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }

        if (crashPoint == CrashPoint.AfterElsaPersistenceBeforeInboxProcessed)
            throw new SimulatedCrashException(crashPoint);

        await MarkProcessedAsync(db, inbox, payload, cancellationToken);
        return DispatchResult.Processed;
    }

    public async Task<DispatchResult> ReconcileAsync(
        Guid domainEventId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _spikeDbFactory.CreateDbContextAsync(cancellationToken);
        var inbox = await db.InboxMessages.AsNoTracking().SingleOrDefaultAsync(
            x => x.DomainEventId == domainEventId,
            cancellationToken);
        if (inbox is null)
            throw new InvalidOperationException("The inbox event was not found.");
        var payload = JsonSerializer.Deserialize<InspectionEventPayload>(inbox.PayloadJson, SpikeJson.Options)
            ?? throw new InvalidOperationException("The inbox payload could not be read.");
        return await DispatchAsync(payload, cancellationToken: cancellationToken);
    }

    public async Task<ShadowSnapshot> GetSnapshotAsync(
        Guid inspectionId,
        CancellationToken cancellationToken = default)
    {
        var instanceId = await FindWorkflowInstanceIdAsync(inspectionId, cancellationToken)
            ?? throw new KeyNotFoundException("The persistent workflow instance was not found.");
        var client = await _workflowRuntime.CreateClientAsync(instanceId, cancellationToken);
        var state = await client.ExportStateAsync(cancellationToken);
        var shadowState = PropertyAsString(state.Properties, WaitForPersistentInspectionLifecycle.ShadowStateProperty)
            ?? string.Empty;
        var lastEventText = PropertyAsString(state.Properties, WaitForPersistentInspectionLifecycle.LastDomainEventIdProperty);
        Guid? lastEventId = Guid.TryParse(lastEventText, out var parsed) ? parsed : null;
        var bookmarks = state.Bookmarks
            .Select(x => BookmarkEventType(x.Payload))
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        return new ShadowSnapshot(instanceId, state.SubStatus.ToString(), shadowState, lastEventId, bookmarks);
    }

    public async Task<long> CountInboxEffectsAsync(
        Guid domainEventId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _spikeDbFactory.CreateDbContextAsync(cancellationToken);
        return await db.InboxMessages.LongCountAsync(x => x.DomainEventId == domainEventId, cancellationToken);
    }

    public async Task<WorkflowOwnedDisposition> DispatchWorkflowOwnedAsync(
        WorkflowOwnedEventEnvelope envelope,
        WorkflowOwnedFaultPoint faultPoint = WorkflowOwnedFaultPoint.None,
        CancellationToken cancellationToken = default)
    {
        var instanceId = await FindWorkflowOwnedInstanceIdAsync(envelope.InspectionId, cancellationToken);
        if (instanceId is null)
        {
            if (envelope.AggregateSequence != 1 || envelope.EventType != InspectionEventTypes.Created)
                return WorkflowOwnedDisposition.InstanceNotFound;

            var client = await _workflowRuntime.CreateClientAsync(cancellationToken);
            var response = await client.CreateAndRunInstanceAsync(
                new CreateAndRunWorkflowInstanceRequest
                {
                    WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(WorkflowOwnedReceiptWorkflow.DefinitionId),
                    CorrelationId = WorkflowOwnedCorrelation(envelope.InspectionId),
                    Input = WorkflowOwnedInput(envelope, faultPoint)
                },
                cancellationToken);
            if (string.IsNullOrWhiteSpace(response.WorkflowInstanceId))
                return WorkflowOwnedDisposition.RuntimeDidNotCommit;
            var created = await GetWorkflowOwnedSnapshotAsync(envelope.InspectionId, cancellationToken);
            return Reflects(created, envelope)
                ? WorkflowOwnedDisposition.Applied
                : WorkflowOwnedDisposition.RuntimeDidNotCommit;
        }

        var before = await GetWorkflowOwnedSnapshotAsync(envelope.InspectionId, cancellationToken);
        var classification = Classify(before, envelope);
        await _stimulusSender.SendAsync(
            WaitForWorkflowOwnedInspectionEvent.BookmarkName,
            new WorkflowOwnedStimulus(WorkflowOwnedCorrelation(envelope.InspectionId)),
            new StimulusMetadata
            {
                WorkflowInstanceId = instanceId,
                CorrelationId = WorkflowOwnedCorrelation(envelope.InspectionId),
                Input = WorkflowOwnedInput(envelope, faultPoint)
            },
            cancellationToken);
        var after = await GetWorkflowOwnedSnapshotAsync(envelope.InspectionId, cancellationToken);
        if (Reflects(after, envelope))
            return classification == WorkflowOwnedDisposition.Duplicate
                ? WorkflowOwnedDisposition.Duplicate
                : WorkflowOwnedDisposition.Applied;
        return Classify(after, envelope);
    }

    public async Task<WorkflowOwnedSnapshot> GetWorkflowOwnedSnapshotAsync(
        Guid inspectionId,
        CancellationToken cancellationToken = default)
    {
        var instanceId = await FindWorkflowOwnedInstanceIdAsync(inspectionId, cancellationToken)
            ?? throw new KeyNotFoundException("The workflow-owned receipt instance was not found.");
        var state = await (await _workflowRuntime.CreateClientAsync(instanceId, cancellationToken))
            .ExportStateAsync(cancellationToken);
        var lastIdText = PropertyAsString(state.Properties, WaitForWorkflowOwnedInspectionEvent.LastDomainEventIdProperty);
        _ = Guid.TryParse(lastIdText, out var lastId);
        var incidents = state.Incidents.Select(x => x.Message ?? "Incident").ToArray();
        return new WorkflowOwnedSnapshot(
            instanceId,
            state.SubStatus.ToString(),
            PropertyAsString(state.Properties, WaitForWorkflowOwnedInspectionEvent.CurrentShadowStateProperty) ?? string.Empty,
            PropertyAsLong(state.Properties, WaitForWorkflowOwnedInspectionEvent.LastAppliedSequenceProperty),
            lastId,
            PropertyAsString(state.Properties, WaitForWorkflowOwnedInspectionEvent.LastPayloadHashProperty) ?? string.Empty,
            state.Bookmarks.Count,
            incidents);
    }

    private async Task<string?> FindWorkflowOwnedInstanceIdAsync(Guid inspectionId, CancellationToken cancellationToken)
    {
        await using var managementDb = await _managementDbFactory.CreateDbContextAsync(cancellationToken);
        return await managementDb.WorkflowInstances.AsNoTracking()
            .Where(x => x.CorrelationId == WorkflowOwnedCorrelation(inspectionId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Dictionary<string, object> WorkflowOwnedInput(
        WorkflowOwnedEventEnvelope envelope,
        WorkflowOwnedFaultPoint faultPoint) => new()
        {
            ["InspectionId"] = WorkflowOwnedCorrelation(envelope.InspectionId),
            ["Event"] = envelope,
            ["FaultPoint"] = faultPoint
        };

    private static WorkflowOwnedDisposition Classify(WorkflowOwnedSnapshot snapshot, WorkflowOwnedEventEnvelope envelope)
    {
        if (envelope.AggregateSequence == snapshot.LastAppliedSequence)
            return envelope.DomainEventId == snapshot.LastDomainEventId && envelope.PayloadHash == snapshot.LastPayloadHash
                ? WorkflowOwnedDisposition.Duplicate
                : WorkflowOwnedDisposition.Conflict;
        if (envelope.AggregateSequence < snapshot.LastAppliedSequence)
            return WorkflowOwnedDisposition.Obsolete;
        if (envelope.AggregateSequence > snapshot.LastAppliedSequence + 1)
            return WorkflowOwnedDisposition.Gap;
        if (snapshot.CurrentShadowState is ShadowStates.Approved or ShadowStates.Rejected)
            return WorkflowOwnedDisposition.TerminalConflict;
        return WorkflowOwnedDisposition.InvalidTransition;
    }

    private static bool Reflects(WorkflowOwnedSnapshot snapshot, WorkflowOwnedEventEnvelope envelope) =>
        snapshot.LastAppliedSequence == envelope.AggregateSequence
        && snapshot.LastDomainEventId == envelope.DomainEventId
        && snapshot.LastPayloadHash == envelope.PayloadHash;

    private static long PropertyAsLong(IDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
            return 0;
        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetInt64(),
            _ => long.Parse(value.ToString()!, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static string WorkflowOwnedCorrelation(Guid inspectionId) => $"owned:{inspectionId:D}";

    private async Task<DispatchResult> ApplyToElsaAsync(
        InspectionEventPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.EventType == InspectionEventTypes.Created)
        {
            if (await FindWorkflowInstanceIdAsync(payload.InspectionId, cancellationToken) is not null)
                return DispatchResult.Duplicate;
            var client = await _workflowRuntime.CreateClientAsync(cancellationToken);
            var correlationId = CanonicalCorrelation(payload.InspectionId);
            var response = await client.CreateAndRunInstanceAsync(
                new CreateAndRunWorkflowInstanceRequest
                {
                    WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(PersistentInspectionWorkflow.DefinitionId),
                    CorrelationId = correlationId,
                    Input = new Dictionary<string, object>
                    {
                        ["InspectionId"] = correlationId,
                        ["ProcessId"] = payload.ProcessId,
                        ["ActorUserId"] = payload.ActorUserId,
                        ["Event"] = payload
                    }
                },
                cancellationToken);
            return string.IsNullOrWhiteSpace(response.WorkflowInstanceId)
                ? DispatchResult.InstanceNotFound
                : DispatchResult.Processed;
        }

        var instanceId = await FindWorkflowInstanceIdAsync(payload.InspectionId, cancellationToken);
        if (instanceId is null)
            return DispatchResult.InstanceNotFound;
        var snapshot = await GetSnapshotAsync(payload.InspectionId, cancellationToken);
        var correlation = CanonicalCorrelation(payload.InspectionId);
        var stimulus = new InspectionStimulus(payload.EventType, correlation);
        var clientState = await (await _workflowRuntime.CreateClientAsync(instanceId, cancellationToken))
            .ExportStateAsync(cancellationToken);
        var bookmark = clientState.Bookmarks.SingleOrDefault(
            x => BookmarkEventType(x.Payload) == payload.EventType);
        if (bookmark is null)
            return DispatchResult.OutOfOrder;

        await _stimulusSender.SendAsync(
            bookmark.Name,
            stimulus,
            new StimulusMetadata
            {
                WorkflowInstanceId = instanceId,
                CorrelationId = correlation,
                Input = new Dictionary<string, object>
                {
                    ["InspectionId"] = correlation,
                    ["ProcessId"] = payload.ProcessId,
                    ["ActorUserId"] = payload.ActorUserId,
                    ["Event"] = payload
                }
            },
            cancellationToken);
        var after = await GetSnapshotAsync(payload.InspectionId, cancellationToken);
        return after.LastDomainEventId == payload.DomainEventId
            ? DispatchResult.Processed
            : DispatchResult.OutOfOrder;
    }

    private async Task<bool> IsReflectedAsync(
        InspectionEventPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await GetSnapshotAsync(payload.InspectionId, cancellationToken);
            return snapshot.LastDomainEventId == payload.DomainEventId;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private async Task MarkProcessedAsync(
        SpikeDbContext db,
        SpikeInboxMessage inbox,
        InspectionEventPayload payload,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(payload.InspectionId, cancellationToken);
        inbox.ProcessedAtUtc = DateTime.UtcNow;
        inbox.Outcome = "Processed";
        inbox.ErrorCode = null;
        var mapping = await db.WorkflowMappings.SingleOrDefaultAsync(
            x => x.InspectionId == payload.InspectionId,
            cancellationToken);
        if (mapping is null)
        {
            mapping = new SpikeWorkflowMapping { InspectionId = payload.InspectionId };
            db.WorkflowMappings.Add(mapping);
        }
        mapping.WorkflowInstanceId = snapshot.WorkflowInstanceId;
        mapping.LastDomainEventId = payload.DomainEventId;
        mapping.CurrentShadowState = snapshot.ShadowState;
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> FindWorkflowInstanceIdAsync(
        Guid inspectionId,
        CancellationToken cancellationToken)
    {
        await using var spikeDb = await _spikeDbFactory.CreateDbContextAsync(cancellationToken);
        var mapped = await spikeDb.WorkflowMappings.AsNoTracking()
            .Where(x => x.InspectionId == inspectionId)
            .Select(x => x.WorkflowInstanceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(mapped))
            return mapped;

        await using var managementDb = await _managementDbFactory.CreateDbContextAsync(cancellationToken);
        return await managementDb.WorkflowInstances.AsNoTracking()
            .Where(x => x.CorrelationId == CanonicalCorrelation(inspectionId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ValidatePayload(InspectionEventPayload payload)
    {
        if (payload.SchemaVersion != 1
            || payload.DomainEventId == Guid.Empty
            || payload.InspectionId == Guid.Empty
            || payload.ProcessId == Guid.Empty
            || payload.ActorUserId == Guid.Empty)
            throw new InvalidOperationException("The event envelope is invalid.");
        if (!InspectionEventTypes.Known.Contains(payload.EventType))
            throw new UnknownEventTypeException(payload.EventType);
        var json = JsonSerializer.Serialize(payload, SpikeJson.Options);
        if (Encoding.UTF8.GetByteCount(json) > 16 * 1024)
            throw new InvalidOperationException("The event payload exceeds 16 KiB.");
    }

    private static string? PropertyAsString(IDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
            return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value.ToString()
        };
    }

    private static string? BookmarkEventType(object? payload) => payload switch
    {
        InspectionStimulus stimulus => stimulus.EventType,
        JsonElement { ValueKind: JsonValueKind.Object } element
            when element.TryGetProperty("eventType", out var eventType) => eventType.GetString(),
        IDictionary<string, object> dictionary when dictionary.TryGetValue("eventType", out var eventType) =>
            eventType?.ToString(),
        _ => null
    };

    public static string CanonicalCorrelation(Guid inspectionId) => inspectionId.ToString("D");

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

public sealed class SimulatedCrashException(CrashPoint point)
    : Exception($"Simulated crash at {point}.")
{
    public CrashPoint Point { get; } = point;
}

public sealed class UnknownEventTypeException(string eventType)
    : Exception($"Unknown event type: {eventType}.");

internal sealed class SqlApplicationLock(SqlConnection connection, string resource) : IAsyncDisposable
{
    public static async Task<SqlApplicationLock> AcquireAsync(
        string connectionString,
        string resource,
        CancellationToken cancellationToken)
    {
        SpikeDatabaseSafety.RequireSafeConnectionString(connectionString);
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!string.Equals(connection.Database, SpikeDatabaseSafety.ExpectedDatabaseName, StringComparison.Ordinal))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("The connected database failed the spike safety guard.");
        }
        await using var command = new SqlCommand("sp_getapplock", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@Resource", resource);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", "Session");
        command.Parameters.AddWithValue("@LockTimeout", 15000);
        var returnValue = command.Parameters.Add("@RETURN_VALUE", System.Data.SqlDbType.Int);
        returnValue.Direction = System.Data.ParameterDirection.ReturnValue;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if ((int)returnValue.Value < 0)
        {
            await connection.DisposeAsync();
            throw new TimeoutException("Could not acquire the durable event lock.");
        }
        return new SqlApplicationLock(connection, resource);
    }

    public async ValueTask DisposeAsync()
    {
        await using var command = new SqlCommand("sp_releaseapplock", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@Resource", resource);
        command.Parameters.AddWithValue("@LockOwner", "Session");
        await command.ExecuteNonQueryAsync();
        await connection.DisposeAsync();
    }
}
