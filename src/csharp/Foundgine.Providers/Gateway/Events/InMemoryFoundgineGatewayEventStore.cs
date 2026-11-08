using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Providers;

namespace Foundgine.Providers.Gateway.Events;

/// <summary>
/// Bounded, process-local event journal for development and single-instance deployments.
/// It is not durable across restarts and must not be presented as a production audit store.
/// </summary>
public sealed class InMemoryFoundgineGatewayEventStore : IFoundgineGatewayEventStore, IFoundgineProvider
{
    public string Id => "foundgine.inmemory.eventstore";
    public string Name => "In-memory gateway event journal";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.EventStore;

    /// <summary>Ordered and replayable within one process; deliberately not durable and not tenant-isolated.</summary>
    public FoundgineProviderCapability Capabilities =>
        FoundgineProviderCapability.OrderedEvents |
        FoundgineProviderCapability.EventReplay |
        FoundgineProviderCapability.TransactionalAppend;

    private readonly object _gate = new();
    private readonly List<FoundgineGatewayEvent> _events = [];
    private readonly int _capacity;
    private long _sequence;

    public InMemoryFoundgineGatewayEventStore(int capacity = 10_000)
    {
        if (capacity < 100) throw new ArgumentOutOfRangeException(nameof(capacity), "Event retention must be at least 100.");
        _capacity = capacity;
    }

    public ValueTask<FoundgineGatewayEvent> AppendAsync(FoundgineGatewayEvent gatewayEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var stored = gatewayEvent with { Sequence = ++_sequence };
            _events.Add(stored);
            if (_events.Count > _capacity) _events.RemoveRange(0, _events.Count - _capacity);
            return ValueTask.FromResult(stored);
        }
    }

    public ValueTask<long> GetLatestSequenceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return ValueTask.FromResult(_sequence);
    }

    public ValueTask<IReadOnlyList<FoundgineGatewayEvent>> ReadAfterAsync(long sequence, int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<FoundgineGatewayEvent> result = _events.Where(x => x.Sequence > sequence)
                .Take(limit).ToArray();
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<IReadOnlyList<FoundgineGatewayEvent>> ReadByCorrelationIdAsync(string correlationId, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ValidateLimit(limit);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<FoundgineGatewayEvent> result = _events
                .Where(x => string.Equals(x.CorrelationId, correlationId, StringComparison.Ordinal))
                .TakeLast(limit).ToArray();
            return ValueTask.FromResult(result);
        }
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
