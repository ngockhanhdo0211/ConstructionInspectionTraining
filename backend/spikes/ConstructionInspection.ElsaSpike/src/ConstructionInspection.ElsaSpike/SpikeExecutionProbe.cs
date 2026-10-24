using System.Collections.Concurrent;

namespace ConstructionInspection.ElsaSpike;

public sealed class SpikeExecutionProbe
{
    private readonly ConcurrentQueue<InspectionEventPayload> _events = new();

    public IReadOnlyCollection<InspectionEventPayload> Events => _events.ToArray();

    public bool SawCancelableToken { get; private set; }

    public void Record(InspectionEventPayload payload, CancellationToken cancellationToken)
    {
        SawCancelableToken |= cancellationToken.CanBeCanceled;
        _events.Enqueue(payload);
    }
}
