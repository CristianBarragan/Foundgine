using System.Text.Json.Serialization.Metadata;

namespace Foundgine.Providers.EventStore;

/// <summary>
/// Foundgine-owned, tenant-scoped, per-stream ordered event log. Payloads are serialized with
/// caller-supplied source-generated <see cref="JsonTypeInfo{T}"/> (no reflection, Native AOT safe).
/// </summary>
public interface IEventStore
{
    /// <summary>Appends idempotently by (tenant, eventId) and returns the stream sequence.</summary>
    Task<long> AppendAsync<T>(
        string tenantId, string streamId, Guid eventId, string eventType,
        DateTimeOffset occurredAt, T payload, JsonTypeInfo<T> payloadTypeInfo,
        string? correlationId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventEnvelope<T>>> ReadAsync<T>(
        string tenantId, string streamId, long afterSequence, int? take,
        JsonTypeInfo<T> payloadTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>Returns the latest <paramref name="take"/> events of one stream with the correlation id, oldest first.</summary>
    Task<IReadOnlyList<EventEnvelope<T>>> ReadByCorrelationAsync<T>(
        string tenantId, string streamId, string correlationId, int take,
        JsonTypeInfo<T> payloadTypeInfo, CancellationToken cancellationToken = default);

    /// <summary>High-water mark of the stream; 0 when empty. Retention never lowers it.</summary>
    Task<long> GetLatestSequenceAsync(
        string tenantId, string streamId, CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset olderThan, int batchSize = 1_000,
        CancellationToken cancellationToken = default);
}
