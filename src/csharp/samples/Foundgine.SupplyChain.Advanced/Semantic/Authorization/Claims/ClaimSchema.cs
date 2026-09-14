using Foundgine.SupplyChain.Advanced.Authorization;

namespace Foundgine.SupplyChain.Advanced.Authorization.Claims;

/// <summary>
/// A claim schema registry: everything <see cref="ClientClaimsValidator"/> needs
/// to validate a raw claim set for one vertical/tenant/version, bound to a
/// <see cref="ClaimSchema"/> instance rather than hard-coded as static fields.
///
/// Each application (or each tenant, or each version of a vertical) builds its
/// own <see cref="ClaimSchema"/> instead of editing a shared static class, so
/// multiple verticals — or multiple identity conventions within one vertical —
/// can coexist without modifying this file.
/// </summary>
public sealed class ClaimSchema
{
    private readonly IReadOnlyDictionary<string, IClaimValidator> _validators;

    public ClaimSchema(
        string verticalName,
        IReadOnlySet<string> reservedIdentityKeys,
        IReadOnlySet<string> hostileReservedIdentityKeys,
        IReadOnlyDictionary<string, IClaimValidator> validators,
        string? expiryKey = null,
        TimeSpan? maxExpiryHorizon = null,
        IReadOnlySet<string>? evidenceKeysBoundByExpiry = null)
    {
        VerticalName = verticalName ?? throw new ArgumentNullException(nameof(verticalName));
        ReservedIdentityKeys = reservedIdentityKeys ?? throw new ArgumentNullException(nameof(reservedIdentityKeys));
        HostileReservedIdentityKeys = hostileReservedIdentityKeys ??
                                      throw new ArgumentNullException(nameof(hostileReservedIdentityKeys));
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        ExpiryKey = expiryKey;
        MaxExpiryHorizon = maxExpiryHorizon;
        EvidenceKeysBoundByExpiry = evidenceKeysBoundByExpiry ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Human-readable name for diagnostics/logging (e.g. "SupplyChain").</summary>
    public string VerticalName { get; }

    /// <summary>
    /// Claim keys that describe identity or privilege directly and can only be
    /// established through authentication, never through a client-supplied claim.
    /// </summary>
    public IReadOnlySet<string> ReservedIdentityKeys { get; }

    /// <summary>
    /// The subset of <see cref="ReservedIdentityKeys"/> that assert privilege or
    /// capability outright (e.g. an admin flag or a permissions list). Presence
    /// of one of these is classified as <see cref="ClaimSpoofingSeverity.Hostile"/>;
    /// other reserved keys (e.g. a client innocently echoing back "tenant") are
    /// classified as <see cref="ClaimSpoofingSeverity.Suspicious"/>. Both are
    /// rejected identically and fail the request closed — the severity exists
    /// for diagnostics/logging, not to relax the security posture.
    /// </summary>
    public IReadOnlySet<string> HostileReservedIdentityKeys { get; }

    /// <summary>The claim key that bounds the validity window of other claims, if any (e.g. "not_after").</summary>
    public string? ExpiryKey { get; }

    /// <summary>
    /// The maximum distance into the future <see cref="ExpiryKey"/> is allowed to
    /// name. Without a ceiling, a caller can hand-write an expiry decades out and
    /// have it accepted as if the evidence were valid indefinitely.
    /// </summary>
    public TimeSpan? MaxExpiryHorizon { get; }

    /// <summary>
    /// Claim keys whose acceptance is evidence bound to <see cref="ExpiryKey"/>:
    /// if the expiry is missing, expired, or exceeds <see cref="MaxExpiryHorizon"/>,
    /// these are retracted along with it.
    /// </summary>
    public IReadOnlySet<string> EvidenceKeysBoundByExpiry { get; }

    public bool IsReserved(string key) => ReservedIdentityKeys.Contains(key);

    public bool IsHostileReserved(string key) => HostileReservedIdentityKeys.Contains(key);

    public IClaimValidator? GetValidator(string key) =>
        _validators.TryGetValue(key, out var validator) ? validator : null;
}