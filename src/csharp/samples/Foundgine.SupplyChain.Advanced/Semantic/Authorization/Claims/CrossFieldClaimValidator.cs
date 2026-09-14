using System.Globalization;
using Foundgine.SupplyChain.Advanced.Authorization;

namespace Foundgine.SupplyChain.Advanced.Authorization.Claims;

/// <summary>
/// Validates relationships between already-format-valid claims, driven by the
/// expiry configuration on a <see cref="ClaimSchema"/> rather than hard-coded
/// key names. Two things can retract an already-accepted expiry-bound claim:
///
///  1. The expiry has already passed — the evidence is stale.
///  2. The expiry is further in the future than <see cref="ClaimSchema.MaxExpiryHorizon"/>
///     allows — without this ceiling, a caller could name an expiry decades out
///     and have evidence trusted as if it were valid indefinitely.
///
/// Either way, every claim in <see cref="ClaimSchema.EvidenceKeysBoundByExpiry"/>
/// that the expiry was meant to bound is retracted along with it.
/// </summary>
public static class CrossFieldClaimValidator
{
    public static void Apply(
        Dictionary<string, string> accepted,
        List<RejectedClaim> rejected,
        IReadOnlyDictionary<string, string> rawClaims,
        ClaimSchema schema,
        DateTimeOffset now)
    {
        if (schema.ExpiryKey is not { } expiryKey)
            return;

        if (!accepted.TryGetValue(expiryKey, out var expiryRaw))
            return;

        if (!DateTimeOffset.TryParse(expiryRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out var expiry))
            return;

        if (expiry < now)
        {
            Retract(accepted, rejected, rawClaims, schema, expiryKey, expiryRaw,
                $"Associated '{expiryKey}' claim ({expiryRaw}) has expired; evidence is stale.");
            rejected.Add(new RejectedClaim(expiryKey, expiryRaw, "Timestamp is in the past."));
            return;
        }

        if (schema.MaxExpiryHorizon is { } horizon && expiry > now + horizon)
        {
            var horizonDescription = DescribeHorizon(horizon);
            Retract(accepted, rejected, rawClaims, schema, expiryKey, expiryRaw,
                $"Associated '{expiryKey}' claim exceeds the maximum evidence validity horizon of {horizonDescription}.");
            rejected.Add(new RejectedClaim(expiryKey, expiryRaw,
                $"Timestamp exceeds the maximum evidence validity horizon of {horizonDescription} from now."));
        }
    }

    private static void Retract(
        Dictionary<string, string> accepted,
        List<RejectedClaim> rejected,
        IReadOnlyDictionary<string, string> rawClaims,
        ClaimSchema schema,
        string expiryKey,
        string expiryRaw,
        string reason)
    {
        accepted.Remove(expiryKey);
        foreach (var evidenceKey in schema.EvidenceKeysBoundByExpiry)
        {
            if (accepted.Remove(evidenceKey))
                rejected.Add(new RejectedClaim(evidenceKey, rawClaims.GetValueOrDefault(evidenceKey), reason));
        }
    }

    private static string DescribeHorizon(TimeSpan horizon) =>
        horizon.TotalDays >= 1
            ? $"{horizon.TotalDays:0.##} day(s)"
            : $"{horizon.TotalHours:0.##} hour(s)";
}