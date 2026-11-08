namespace Foundgine.Providers.Gateway;

/// <summary>Configuration for the provider-package gateway facade.</summary>
public sealed class FoundgineGatewayOptions
{
    private readonly List<FoundgineGatewayCapability> _capabilities = [];

    /// <summary>Stable gateway name returned by <see cref="IFoundgineGateway.Describe"/>.</summary>
    public string Name { get; set; } = "Foundgine Gateway";

    /// <summary>Version of the public gateway contract, independent of the NuGet package version.</summary>
    public string ContractVersion { get; set; } = "1.0";

    /// <summary>Maximum number of recent events retained per live subscriber.</summary>
    public int SubscriberBufferCapacity { get; set; } = 256;

    /// <summary>Events read from the journal per round trip while replaying or back-filling a subscriber (1–10,000).</summary>
    public int ReplayPageSize { get; set; } = 1_000;

    /// <summary>SSE stream behaviour: heartbeat, replay window, audience rules.</summary>
    public Events.FoundgineGatewayEventStreamOptions EventStream { get; } = new();

    /// <summary>Capabilities made discoverable by the gateway.</summary>
    public IReadOnlyList<FoundgineGatewayCapability> Capabilities => _capabilities;

    /// <summary>Adds a capability to the discovery catalog. IDs are unique, case-insensitively.</summary>
    public FoundgineGatewayOptions AddCapability(FoundgineGatewayCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (string.IsNullOrWhiteSpace(capability.Id))
            throw new ArgumentException("A capability ID is required.", nameof(capability));
        if (_capabilities.Any(existing => string.Equals(
                existing.Id, capability.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Gateway capability '{capability.Id}' is already registered.");
        _capabilities.Add(capability);
        return this;
    }
}
