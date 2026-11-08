using System.Text.Json.Serialization;

namespace Foundgine.Providers.Gateway;

/// <summary>Describes one capability exposed through the Foundgine gateway.</summary>
public sealed record FoundgineGatewayCapability(
    string Id,
    string Name,
    string Version,
    string Kind,
    string Description,
    IReadOnlyList<string> Operations,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Describes the gateway surface without exposing provider implementation details.</summary>
public sealed record FoundgineGatewayDescription(
    string Name,
    string ContractVersion,
    IReadOnlyList<FoundgineGatewayCapability> Capabilities);

/// <summary>Stable gateway facade for capability discovery and live event subscriptions.</summary>
public interface IFoundgineGateway
{
    FoundgineGatewayDescription Describe();
    FoundgineGatewayCapability? FindCapability(string id);
    IAsyncEnumerable<FoundgineGatewayEvent> SubscribeAsync(
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<FoundgineGatewayEvent> SubscribeAfterAsync(long lastSequence,
        CancellationToken cancellationToken = default);
}

/// <summary>Publishes lifecycle and domain-neutral integration events to gateway subscribers.</summary>
public interface IFoundgineGatewayEventPublisher
{
    ValueTask PublishAsync(
        FoundgineGatewayEventType type,
        string subject,
        string? correlationId = null,
        string? providerId = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Receives gateway events for hooks such as audit forwarding and cache invalidation.</summary>
public interface IFoundgineGatewayHook
{
    ValueTask OnEventAsync(
        FoundgineGatewayEvent gatewayEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>Marks a hook whose success is required before an event is delivered to other hooks/subscribers.</summary>
public interface IFoundgineGatewayCriticalHook : IFoundgineGatewayHook
{
}

/// <summary>Event envelope emitted by the gateway. It deliberately excludes request payloads and credentials.</summary>
public sealed record FoundgineGatewayEvent(
    long Sequence,
    DateTimeOffset OccurredAt,
    FoundgineGatewayEventType Type,
    string Subject,
    string? CorrelationId,
    string? ProviderId,
    IReadOnlyDictionary<string, string> Attributes)
{
    /// <summary>Stable event identity available to critical hooks before the event store assigns its sequence.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>Built-in event categories. Applications may use <see cref="Custom"/> with a namespaced subject.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FoundgineGatewayEventType>))]
public enum FoundgineGatewayEventType
{
    CapabilityChanged,
    SemanticModelChanged,
    ProviderHealthChanged,
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    AuthorizationDenied,
    ProviderRegistered,
    ProviderUnregistered,
    Custom
}
