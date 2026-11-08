namespace Foundgine.Providers.Gateway.Providers;

/// <summary>
/// Foundgine-owned provider identity and capability declaration. Infrastructure adapters
/// (PostgreSQL, Temporal, Kafka, Keycloak, ...) implement this next to their role contract.
/// </summary>
public interface IFoundgineProvider
{
    /// <summary>Stable, unique provider id, for example <c>foundgine.postgres.eventstore</c>.</summary>
    string Id { get; }
    string Name { get; }
    string Version { get; }
    FoundgineProviderKind Kind { get; }
    FoundgineProviderCapability Capabilities { get; }
}

public static class FoundgineProviderExtensions
{
    /// <summary>Projects a provider to the transport-neutral registry descriptor.</summary>
    public static FoundgineGatewayProviderDescriptor ToDescriptor(this IFoundgineProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new FoundgineGatewayProviderDescriptor(
            provider.Id, provider.Name, provider.Version,
            FoundgineProviderCapabilityNames.ToNames(provider.Capabilities));
    }
}
