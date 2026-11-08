namespace Foundgine.Providers.EventStore;

public sealed record EventEnvelope<T>(
    Guid EventId, string TenantId, string StreamId, long Sequence,
    string EventType, DateTimeOffset OccurredAt, T Payload, string? CorrelationId = null);
