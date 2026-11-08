namespace Foundgine.Providers.Gateway.Audit;

/// <summary>
/// Durable audit persistence boundary. Implementations should append an immutable, versioned record
/// to the application's audit store and enforce its retention/integrity policy.
/// </summary>
public interface IFoundgineGatewayAuditSink
{
    ValueTask AppendAsync(
        FoundgineGatewayEvent gatewayEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>Forwards gateway events to a durable audit sink before live subscriber delivery.</summary>
public sealed class FoundgineGatewayAuditHook : IFoundgineGatewayCriticalHook
{
    private readonly IFoundgineGatewayAuditSink _sink;

    public FoundgineGatewayAuditHook(IFoundgineGatewayAuditSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public ValueTask OnEventAsync(
        FoundgineGatewayEvent gatewayEvent,
        CancellationToken cancellationToken = default) =>
        _sink.AppendAsync(gatewayEvent, cancellationToken);
}
