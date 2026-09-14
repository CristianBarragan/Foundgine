using Foundgine.SupplyChain.Advanced.Authorization;

namespace Foundgine.SupplyChain.Advanced.Authorization.Claims;

/// <summary>How severe a reserved-identity-key spoofing attempt looks, for diagnostics/logging.</summary>
/// <remarks>
/// Every non-<see cref="None"/> severity is rejected identically: the whole
/// request fails closed, exactly as if the caller had tried to assert
/// identity directly, because a client that tries this once has demonstrated
/// intent that should not be trusted with partial processing. The severity
/// distinguishes an outright privilege-assertion (<see cref="Hostile"/>) from
/// a client plausibly just echoing an identity-shaped field back
/// (<see cref="Suspicious"/>) so operators can triage and alert differently,
/// without weakening the fail-closed behavior itself.
/// </remarks>
public enum ClaimSpoofingSeverity
{
    None,
    Suspicious,
    Hostile
}

/// <summary>The outcome of checking a raw claim set for reserved-identity-key spoofing.</summary>
public sealed record SpoofingCheckResult(ClaimSpoofingSeverity Severity, IReadOnlyList<RejectedClaim> Rejected)
{
    public bool IsSpoofingAttempt => Severity != ClaimSpoofingSeverity.None;

    public static SpoofingCheckResult None { get; } = new(ClaimSpoofingSeverity.None, []);
}

/// <summary>
/// Checks a raw, client-supplied claim set for keys that assert identity or
/// privilege directly. This is deliberately isolated from format validation
/// and cross-field validation: identity spoofing is a whole-request failure
/// with its own severity model, not just another kind of malformed claim.
/// </summary>
public static class IdentitySpoofingValidator
{
    public static SpoofingCheckResult Check(IReadOnlyDictionary<string, string> rawClaims, ClaimSchema schema)
    {
        var spoofedKeys = rawClaims.Keys.Where(schema.IsReserved).ToArray();
        if (spoofedKeys.Length == 0)
            return SpoofingCheckResult.None;

        var severity = spoofedKeys.Any(schema.IsHostileReserved)
            ? ClaimSpoofingSeverity.Hostile
            : ClaimSpoofingSeverity.Suspicious;

        var rejections = spoofedKeys
            .Select(key => new RejectedClaim(
                key,
                rawClaims[key],
                "Identity/privilege must come from authentication, not from a client-supplied claim.",
                severity))
            .ToArray();

        return new SpoofingCheckResult(severity, rejections);
    }
}
