namespace Foundgine.SupplyChain.Advanced.Authorization.Claims;

/// <summary>
/// The recognized, non-identity claim keys for the SupplyChain vertical.
/// Using this enum instead of raw strings gives compile-time safety and
/// discoverability when policy code (e.g. <c>SupplyChainAuthorization</c>)
/// needs to refer to a claim key, while <see cref="SupplyChainClaimKeys.WireName"/>
/// stays the single place that maps each member to the string that actually
/// travels over the wire.
/// </summary>
public enum SupplyChainClaimKey
{
    Scope,
    Warehouse,
    MaxRows,
    Reason,
    ChangeTicket,
    NotAfter
}

public static class SupplyChainClaimKeys
{
    public static string WireName(this SupplyChainClaimKey key) => key switch
    {
        SupplyChainClaimKey.Scope => "scope",
        SupplyChainClaimKey.Warehouse => "warehouse",
        SupplyChainClaimKey.MaxRows => "max_rows",
        SupplyChainClaimKey.Reason => "reason",
        SupplyChainClaimKey.ChangeTicket => "change_ticket",
        SupplyChainClaimKey.NotAfter => "not_after",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unrecognized SupplyChain claim key.")
    };
}