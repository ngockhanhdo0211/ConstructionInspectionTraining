using System.Collections.Concurrent;
using Elsa.Extensions;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConstructionInspection.ElsaSpike;

public enum DispatchResult
{
    Processed,
    Duplicate,
    NoMatchingBookmark,
    UnknownEvent,
    InstanceNotFound
}

public sealed class InspectionSpikeHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IStimulusSender _stimulusSender;
    private readonly ConcurrentDictionary<Guid, string> _instances = new();
    private readonly ConcurrentDictionary<Guid, byte> _processedEvents = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    private InspectionSpikeHost(IHost host)
    {
        _host = host;
        _workflowRuntime = host.Services.GetRequiredService<IWorkflowRuntime>();
        _stimulusSender = host.Services.GetRequiredService<IStimulusSender>();
    }

    public SpikeExecutionProbe Probe => _host.Services.GetRequiredService<SpikeExecutionProbe>();

    public static async Task<InspectionSpikeHost> CreateAsync(CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<SpikeExecutionProbe>();
        builder.Services.AddElsa(elsa => elsa
            .AddActivity<WaitForInspectionLifecycle>()
            .AddWorkflow<InspectionLifecycleWorkflow>());

        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        return new InspectionSpikeHost(host);
    }

    public async Task<string> StartAsync(
        Guid inspectionId,
        Guid processId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var correlationId = CanonicalCorrelation(inspectionId);
        var client = await _workflowRuntime.CreateClientAsync(cancellationToken);
        var response = await client.CreateAndRunInstanceAsync(
            new CreateAndRunWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(
                    InspectionLifecycleWorkflow.DefinitionId),
                CorrelationId = correlationId,
                Input = new Dictionary<string, object>
                {
                    ["InspectionId"] = correlationId,
                    ["ProcessId"] = processId,
                    ["ActorUserId"] = actorUserId
                }
            },
            cancellationToken);

        var instanceId = response.WorkflowInstanceId
            ?? throw new InvalidOperationException("Elsa did not return a workflow instance ID.");
        if (!_instances.TryAdd(inspectionId, instanceId))
            throw new InvalidOperationException("An E0 workflow already exists for this Inspection.");

        return instanceId;
    }

    public async Task<DispatchResult> SendAsync(
        InspectionEventPayload payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!InspectionEventTypes.Known.Contains(payload.EventType))
            return DispatchResult.UnknownEvent;
        if (!_instances.TryGetValue(payload.InspectionId, out var instanceId))
            return DispatchResult.InstanceNotFound;

        var gate = _locks.GetOrAdd(payload.InspectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_processedEvents.ContainsKey(payload.DomainEventId))
                return DispatchResult.Duplicate;

            var state = await GetStateAsync(payload.InspectionId, cancellationToken);
            var correlationId = CanonicalCorrelation(payload.InspectionId);
            var stimulus = new InspectionStimulus(payload.EventType, correlationId);
            var matchingBookmark = state.Bookmarks.SingleOrDefault(bookmark => Equals(bookmark.Payload, stimulus));
            if (matchingBookmark is null)
                return DispatchResult.NoMatchingBookmark;

            if (!_processedEvents.TryAdd(payload.DomainEventId, 0))
                return DispatchResult.Duplicate;

            try
            {
                await _stimulusSender.SendAsync(
                    matchingBookmark.Name,
                    stimulus,
                    new StimulusMetadata
                    {
                        WorkflowInstanceId = instanceId,
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

                if (!Probe.Events.Any(item => item.DomainEventId == payload.DomainEventId))
                {
                    _processedEvents.TryRemove(payload.DomainEventId, out _);
                    return DispatchResult.NoMatchingBookmark;
                }
                return DispatchResult.Processed;
            }
            catch
            {
                _processedEvents.TryRemove(payload.DomainEventId, out _);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Elsa.Workflows.State.WorkflowState> GetStateAsync(
        Guid inspectionId,
        CancellationToken cancellationToken = default)
    {
        if (!_instances.TryGetValue(inspectionId, out var instanceId))
            throw new KeyNotFoundException("The E0 workflow instance was not found.");

        return await GetStateByInstanceIdAsync(instanceId, cancellationToken);
    }

    public async Task<Elsa.Workflows.State.WorkflowState> GetStateByInstanceIdAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var client = await _workflowRuntime.CreateClientAsync(instanceId, cancellationToken);
        return await client.ExportStateAsync(cancellationToken);
    }

    public static string CanonicalCorrelation(Guid inspectionId) => inspectionId.ToString("D");

    public ValueTask DisposeAsync()
    {
        foreach (var gate in _locks.Values)
            gate.Dispose();
        _host.Dispose();
        return ValueTask.CompletedTask;
    }
}
