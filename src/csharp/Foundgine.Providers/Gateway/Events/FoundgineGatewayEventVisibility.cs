using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Providers.Gateway.Events;

/// <summary>Reserved attribute keys used for audience scoping. They never leave the server.</summary>
public static class FoundgineGatewayEventAttributes
{
    /// <summary>Event is visible only to the principal (tenant + subject) whose digest it carries.</summary>
    public const string OwnerDigest = "owner.digest";
    /// <summary>Event is visible to every principal of the tenant whose digest it carries.</summary>
    public const string TenantDigest = "tenant.digest";

    internal static bool IsReserved(string key) =>
        string.Equals(key, OwnerDigest, StringComparison.Ordinal) ||
        string.Equals(key, TenantDigest, StringComparison.Ordinal);
}

/// <summary>The caller as the event stream sees it. Digests come from <see cref="FoundgineGatewayScopeDigester"/>.</summary>
public readonly record struct FoundgineGatewayViewer(string OwnerDigest, string TenantDigest, bool IsOperator);

/// <summary>
/// Fail-closed audience rules for the gateway event stream:
/// owner-scoped events reach only that owner; tenant-scoped events reach that tenant; discovery-change events
/// (<see cref="FoundgineGatewayEventType.CapabilityChanged"/>, <see cref="FoundgineGatewayEventType.SemanticModelChanged"/>)
/// reach every authenticated caller; every other unscoped event is a system event for operators only.
/// Operators do not see other tenants' scoped events.
/// </summary>
public static class FoundgineGatewayEventVisibility
{
    public static bool IsVisible(FoundgineGatewayEvent gatewayEvent, in FoundgineGatewayViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        if (gatewayEvent.Attributes.TryGetValue(FoundgineGatewayEventAttributes.OwnerDigest, out var owner))
            return Equal(owner, viewer.OwnerDigest);
        if (gatewayEvent.Attributes.TryGetValue(FoundgineGatewayEventAttributes.TenantDigest, out var tenant))
            return Equal(tenant, viewer.TenantDigest);
        return gatewayEvent.Type is FoundgineGatewayEventType.CapabilityChanged
                   or FoundgineGatewayEventType.SemanticModelChanged
               || viewer.IsOperator;
    }

    /// <summary>Removes reserved scoping attributes before an event is serialized to a client.</summary>
    public static FoundgineGatewayEvent ToPublic(FoundgineGatewayEvent gatewayEvent)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        if (!gatewayEvent.Attributes.Keys.Any(FoundgineGatewayEventAttributes.IsReserved)) return gatewayEvent;
        return gatewayEvent with
        {
            Attributes = gatewayEvent.Attributes
                .Where(x => !FoundgineGatewayEventAttributes.IsReserved(x.Key))
                .ToDictionary(x => x.Key, x => x.Value)
        };
    }

    private static bool Equal(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

/// <summary>
/// Produces scope digests. Without a key the owner digest is the legacy unkeyed SHA-256 (compatible with events
/// already stored); configure <see cref="FoundgineGatewayEventStreamOptions.SetScopeDigestKey"/> to use HMAC-SHA-256 so
/// digests persisted in journals and audit sinks cannot be guessed from known tenant/subject values.
/// Changing the key stops old events from matching their owners. Owner and tenant digests use different hash
/// functions/keys, so one can never equal the other.
/// </summary>
public sealed class FoundgineGatewayScopeDigester
{
    private readonly byte[]? _ownerKey;
    private readonly byte[] _tenantKey;

    public FoundgineGatewayScopeDigester(FoundgineGatewayEventStreamOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _ownerKey = options.ScopeDigestKey is { IsEmpty: false } key
            ? key.ToArray()
            : null;

        var domain = "foundgine.tenant.v1"u8;

        _tenantKey = _ownerKey is null
            ? domain.ToArray()
            : HMACSHA256.HashData(_ownerKey, domain);
    }

    public string Owner(string? tenant, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var bytes = Encoding.UTF8.GetBytes($"{tenant ?? string.Empty}\0{subject}");
        return Convert.ToHexString(_ownerKey is null ? SHA256.HashData(bytes) : HMACSHA256.HashData(_ownerKey, bytes));
    }

    public string Tenant(string? tenant) =>
        Convert.ToHexString(HMACSHA256.HashData(_tenantKey, Encoding.UTF8.GetBytes(tenant ?? string.Empty)));
}
