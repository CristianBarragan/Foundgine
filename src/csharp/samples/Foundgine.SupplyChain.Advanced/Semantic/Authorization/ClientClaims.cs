using Foundgine.SupplyChain.Advanced.Authorization.Claims;

namespace Foundgine.SupplyChain.Advanced.Authorization;

/// <summary>
/// A single rejected client claim, kept for audit/diagnostic purposes. The
/// MCP tools in this sample return these to the caller so the adversarial
/// client (and this file's own documentation) can show exactly why a claim
/// was not honored. <see cref="Severity"/> is <see cref="ClaimSpoofingSeverity.None"/>
/// for ordinary format/cross-field rejections and only set to
/// <see cref="ClaimSpoofingSeverity.Suspicious"/> or <see cref="ClaimSpoofingSeverity.Hostile"/>
/// for reserved-identity-key spoofing attempts.
/// </summary>
public sealed record RejectedClaim(
    string Key,
    string? Value,
    string Reason,
    ClaimSpoofingSeverity Severity = ClaimSpoofingSeverity.None);

/// <summary>
/// The result of validating an untrusted, client-supplied claim set. Only
/// <see cref="Accepted"/> is ever handed to <see cref="SupplyChainAuthorization"/>.
/// Nothing in <see cref="Rejected"/> is used for any authorization decision.
/// </summary>
public sealed record ClaimsValidationResult(
    IReadOnlyDictionary<string, string> Accepted,
    IReadOnlyList<RejectedClaim> Rejected,
    ClaimSpoofingSeverity SpoofingSeverity,
    IReadOnlyDictionary<string, object>? TypedAccepted = null)
{
    /// <summary>
    /// True for any reserved-identity-key spoofing attempt, regardless of
    /// severity. Every such attempt fails the whole request closed the same
    /// way; <see cref="SpoofingSeverity"/> exists only to distinguish
    /// severities for diagnostics/logging.
    /// </summary>
    public bool IsSpoofingAttempt => SpoofingSeverity != ClaimSpoofingSeverity.None;

    public static ClaimsValidationResult Empty { get; } =
        new(new Dictionary<string, string>(), [], ClaimSpoofingSeverity.None);
}

/// <summary>
/// Validates claims sent by the MCP caller itself, as distinct from the
/// server-derived identity produced by <c>Authenticate(actor, token)</c>.
///
/// The distinction matters: <b>identity</b> (tenant, role) is never taken
/// from the caller — it is resolved server-side from the actor/token pair,
/// exactly as before this feature was added. <b>Claims</b> are additional,
/// caller-asserted context (scope narrowing, resource scoping, operational
/// evidence) that the caller volunteers on top of that identity.
///
/// Because claims arrive over an untrusted transport (any MCP client can
/// send any JSON it likes), this validator treats every claim as hostile
/// until proven otherwise:
///
///  1. Reserved identity keys (role, tenant, actor, admin flags, ...) are
///     never accepted under any circumstances, even if their value happens
///     to match the caller's real identity. Their mere presence is treated
///     as a spoofing attempt and fails the entire request closed — see
///     <see cref="IdentitySpoofingValidator"/>.
///  2. Recognized non-identity keys are validated against a strict format
///     for that key, via the per-key <see cref="IClaimValidator"/>s
///     registered on the active <see cref="ClaimSchema"/>. A malformed
///     value is rejected individually; it does not by itself fail the rest
///     of the request, but any privilege that depended on it is then
///     evaluated as if the claim were absent.
///  3. Unrecognized keys are dropped individually (fail-closed on trust,
///     fail-open on noise): the request proceeds without them, and the
///     rejection is reported back so the caller can see what was ignored.
///  4. Claims with cross-field relationships (currently: evidence claims
///     bounded by an expiry) are reconciled by <see cref="CrossFieldClaimValidator"/>,
///     including a maximum expiry horizon so a caller cannot hand-write an
///     expiry far enough out to make evidence effectively permanent.
///  5. Every accepted claim can only ever narrow what the policy already
///     allows for the authenticated role. Claims are never additive to
///     privilege — see <see cref="SupplyChainAuthorization"/> for how
///     each accepted claim is consumed.
///
/// This class itself is a thin orchestrator over the components above and
/// over <see cref="ClaimSchema"/>: the actual reserved keys, recognized keys,
/// formats, and expiry rules are SupplyChain-specific data supplied by
/// <see cref="SupplyChainClaimSchema.Default"/>, used by the two-argument
/// overload of <c>Validate</c>. A different vertical, tenant, or schema
/// version can call the three-argument overload with its own
/// <see cref="ClaimSchema"/> instead, and every rule below applies unchanged.
/// </summary>
public static class ClientClaimsValidator
{
    /// <summary>Validates a raw claim set against the default SupplyChain claim schema.</summary>
    public static ClaimsValidationResult Validate(
        IReadOnlyDictionary<string, string>? rawClaims,
        DateTimeOffset now) =>
        Validate(rawClaims, now, SupplyChainClaimSchema.Default);

    /// <summary>Validates a raw claim set against an explicitly supplied claim schema.</summary>
    public static ClaimsValidationResult Validate(
        IReadOnlyDictionary<string, string>? rawClaims,
        DateTimeOffset now,
        ClaimSchema schema)
    {
        if (rawClaims is null || rawClaims.Count == 0)
            return ClaimsValidationResult.Empty;

        // Step 1: any reserved identity key present at all is a hard, whole-request
        // failure. We do not selectively drop it and continue, because a caller
        // that tries this once is demonstrating intent to spoof identity, and
        // partially processing the rest of the call would still leak information
        // about which other claims *would* have been honored.
        var spoofing = IdentitySpoofingValidator.Check(rawClaims, schema);
        if (spoofing.IsSpoofingAttempt)
            return new ClaimsValidationResult(new Dictionary<string, string>(), spoofing.Rejected, spoofing.Severity);

        var accepted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var typedAccepted = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<RejectedClaim>();

        foreach (var (key, value) in rawClaims)
        {
            var validator = schema.GetValidator(key);
            if (validator is null)
            {
                rejected.Add(new RejectedClaim(key, value, "Unrecognized claim key; ignored."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                rejected.Add(new RejectedClaim(key, value, "Value is empty."));
                continue;
            }

            var (ok, reason, typedValue) = validator.Validate(value);
            if (!ok)
            {
                rejected.Add(new RejectedClaim(key, value, reason!));
                continue;
            }

            accepted[key] = value;
            if (typedValue is not null)
                typedAccepted[key] = typedValue;
        }

        // Step 2: cross-field validation, e.g. "not_after" only makes sense
        // paired with the evidence it is meant to bound.
        CrossFieldClaimValidator.Apply(accepted, rejected, rawClaims, schema, now);
        foreach (var key in typedAccepted.Keys.Where(k => !accepted.ContainsKey(k)).ToArray())
            typedAccepted.Remove(key);

        return new ClaimsValidationResult(accepted, rejected, ClaimSpoofingSeverity.None, typedAccepted);
    }
}
