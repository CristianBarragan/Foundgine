namespace Foundgine.Providers.Outbox;

/// <summary>
/// A message to be delivered to infrastructure (broker, webhook, workflow signal) with at-least-once semantics.
/// <paramref name="PayloadJson"/> is already serialized by the caller with a source-generated context.
/// Consumers must de-duplicate by <paramref name="Id"/>.
/// </summary>
public sealed record FoundgineOutboxMessage(
    Guid Id,
    string TenantId,
    string Topic,
    string PayloadJson,
    string? Key = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    DateTimeOffset? CreatedAt = null);

/// <summary>A time-limited claim on a message. Complete/Fail only succeed for the current token holder.</summary>
public sealed record FoundgineOutboxLease(FoundgineOutboxMessage Message, int Attempt, Guid LeaseToken);

/// <summary>Writer side. Implementations that offer <c>TransactionalOutbox</c> also expose a way to enlist in the caller's transaction.</summary>
public interface IFoundgineGatewayOutbox
{
    /// <summary>Idempotent by (tenant, message id). Not atomic with any caller transaction unless the implementation says so.</summary>
    ValueTask EnqueueAsync(FoundgineOutboxMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Dispatcher-facing side: lease, acknowledge, retry or dead-letter.</summary>
public interface IFoundgineGatewayOutboxStore
{
    ValueTask<IReadOnlyList<FoundgineOutboxLease>> LeaseAsync(
        int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <returns>False when the lease was lost (expired and re-leased) or the message is already finished.</returns>
    ValueTask<bool> CompleteAsync(
        FoundgineOutboxLease lease, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <param name="error"></param>
    /// <param name="retryAt">When to try again; <c>null</c> moves the message to the dead letter state.</param>
    /// <param name="lease"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    ValueTask<bool> FailAsync(
        FoundgineOutboxLease lease, string error, DateTimeOffset? retryAt, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>Broker adapter (Kafka, RabbitMQ, Service Bus, ...). Throw to signal failure; the dispatcher retries.</summary>
public interface IFoundgineOutboxPublisher
{
    ValueTask PublishAsync(FoundgineOutboxMessage message, CancellationToken cancellationToken = default);
}
