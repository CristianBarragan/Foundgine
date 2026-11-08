using Foundgine.Providers.Gateway;

namespace Foundgine.Providers.Gateway.Events;

/// <summary>
/// Append-only gateway event journal. Production implementations should use a durable store or broker-backed
/// log and guarantee monotonically increasing sequence values plus atomic append semantics.
/// </summary>
public interface IFoundgineGatewayEventStore
{
    ValueTask<FoundgineGatewayEvent> AppendAsync(FoundgineGatewayEvent gatewayEvent,
        CancellationToken cancellationToken = default);

    ValueTask<long> GetLatestSequenceAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<FoundgineGatewayEvent>> ReadAfterAsync(long sequence, int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<FoundgineGatewayEvent>> ReadByCorrelationIdAsync(string correlationId, int limit,
        CancellationToken cancellationToken = default);
}
