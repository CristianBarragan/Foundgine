using System.Globalization;
using System.Text.RegularExpressions;
using Foundgine.SupplyChain.Advanced.Authorization;

namespace Foundgine.SupplyChain.Advanced.Authorization.Claims;

/// <summary>
/// Builds the <see cref="ClaimSchema"/> for the SupplyChain vertical. This is
/// the only file that encodes SupplyChain-specific claim rules; everything
/// that consumes a <see cref="ClaimSchema"/> (identity spoofing checks,
/// format validation, cross-field/expiry validation) is generic and would
/// work unchanged against a different vertical's schema.
/// </summary>
public static class SupplyChainClaimSchema
{
    private static readonly Regex ChangeTicketPattern = new(@"^CHG-\d{4,}$", RegexOptions.Compiled);

    /// <summary>
    /// The default schema instance used by <see cref="ClientClaimsValidator"/>
    /// when none is supplied explicitly.
    /// </summary>
    public static ClaimSchema Default { get; } = Build();

    private static ClaimSchema Build()
    {
        // Claim keys that describe identity or privilege directly. These can
        // only be established through authentication, never through a claim.
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "role",
            "tenant",
            "tenantid",
            "actor",
            "isadmin",
            "admin",
            "permissions",
            "capabilities",
            "scopes"
        };

        // Keys among the above that assert privilege/capability outright, as
        // opposed to identity fields a legitimate client might mistakenly
        // echo back. See ClaimSpoofingSeverity for how this distinction is used.
        var hostile = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "isadmin",
            "admin",
            "permissions",
            "capabilities",
            "scopes"
        };

        var validators = new Dictionary<string, IClaimValidator>(StringComparer.OrdinalIgnoreCase)
        {
            [SupplyChainClaimKey.Scope.WireName()] = new DelegateClaimValidator<string>(
                SupplyChainClaimKey.Scope.WireName(),
                value => value is "read-only" or "full"
                    ? (true, null, value)
                    : (false, "Must be 'read-only' or 'full'.", null)),

            [SupplyChainClaimKey.Warehouse.WireName()] = new DelegateClaimValidator<int>(
                SupplyChainClaimKey.Warehouse.WireName(),
                value => int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var warehouseId) && warehouseId > 0
                    ? (true, null, warehouseId)
                    : (false, "Must be a positive integer warehouse id.", 0)),

            [SupplyChainClaimKey.MaxRows.WireName()] = new DelegateClaimValidator<int>(
                SupplyChainClaimKey.MaxRows.WireName(),
                value => int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var maxRows) && maxRows is > 0 and <= 10_000
                    ? (true, null, maxRows)
                    : (false, "Must be a positive integer no greater than 10,000.", 0)),

            [SupplyChainClaimKey.Reason.WireName()] = new DelegateClaimValidator<string>(
                SupplyChainClaimKey.Reason.WireName(),
                value => value.Trim().Length is >= 8 and <= 240
                    ? (true, null, value)
                    : (false, "Must be between 8 and 240 characters.", null)),

            [SupplyChainClaimKey.ChangeTicket.WireName()] = new DelegateClaimValidator<string>(
                SupplyChainClaimKey.ChangeTicket.WireName(),
                value => ChangeTicketPattern.IsMatch(value)
                    ? (true, null, value)
                    : (false, "Must match 'CHG-####' (four or more digits).", null)),

            [SupplyChainClaimKey.NotAfter.WireName()] = new DelegateClaimValidator<DateTimeOffset>(
                SupplyChainClaimKey.NotAfter.WireName(),
                value => DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var notAfter)
                    ? (true, null, notAfter)
                    : (false, "Must be a valid ISO-8601 timestamp.", default))
        };

        return new ClaimSchema(
            verticalName: "SupplyChain",
            reservedIdentityKeys: reserved,
            hostileReservedIdentityKeys: hostile,
            validators: validators,
            expiryKey: SupplyChainClaimKey.NotAfter.WireName(),
            // Evidence claims (reason, change_ticket) are only ever meaningful
            // alongside a bounded, recent expiry — without a ceiling here a
            // caller could set not_after decades out and have the evidence
            // trusted as if it never expired.
            maxExpiryHorizon: TimeSpan.FromDays(7),
            evidenceKeysBoundByExpiry: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SupplyChainClaimKey.Reason.WireName(),
                SupplyChainClaimKey.ChangeTicket.WireName()
            });
    }
}
