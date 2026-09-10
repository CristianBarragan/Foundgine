namespace Foundgine.SupplyChain.Application;

public interface ICapabilityAuthorizer
{
    void Demand(string actor, string token, string capability, int? customerId = null);
    void Authenticate(string actor, string token);
}

public sealed class SupplyChainAuthorizer : ICapabilityAuthorizer
{
    // Demo credential store. In a real deployment this is a proper identity
    // provider (JWT issuer, OAuth, API-key vault with hashed secrets) - never
    // a static in-code table. This exists only so the sample requires *some*
    // proof of identity instead of trusting a caller-supplied actor string.
    private static readonly Dictionary<string, string> ActorTokens = new(StringComparer.Ordinal)
    {
        ["alice"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_ALICE") ?? "alice-demo-token",
        ["bob"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_BOB") ?? "bob-demo-token",
        ["carol"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_CAROL") ?? "carol-demo-token",
        ["dave"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_DAVE") ?? "dave-demo-token",
        ["admin"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_ADMIN") ?? "admin-demo-token",
    };

    // Fixed, server-side actor -> customer mapping. Nobody gets to grant
    // themselves an arbitrary customerId by encoding it into their own actor
    // string (the previous "customerN" pattern let anyone claim any
    // customer's identity with no verification at all).
    private static readonly Dictionary<string, int> ActorCustomerMap = new(StringComparer.Ordinal)
    {
        ["alice"] = 1,
        ["bob"] = 2,
    };

    private static readonly HashSet<string> CustomerScopedCapabilities = new(StringComparer.Ordinal)
    {
        "get_my_orders", "get_order", "get_shipment", "place_order", "cancel_order"
    };

    public void Authenticate(string actor, string token)
    {
        if (string.IsNullOrEmpty(actor)
            || string.IsNullOrEmpty(token)
            || !ActorTokens.TryGetValue(actor, out var expectedToken)
            || !FixedTimeEquals(token, expectedToken))
        {
            // Same generic message whether the actor exists or not, so the
            // error itself can't be used to enumerate valid actor names.
            throw new UnauthorizedAccessException("Invalid actor credentials.");
        }
    }

    public void Demand(string actor, string token, string capability, int? customerId = null)
    {
        Authenticate(actor, token);

        var allowed = actor switch
        {
            "alice" => new[]
                { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order" },
            "bob" => new[]
            {
                "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order",
                "list_customers"
            },
            "carol" => new[]
                { "get_product", "get_inventory", "update_inventory", "create_shipment", "update_shipment" },
            "dave" => new[] { "get_product", "get_inventory", "list_products", "list_suppliers", "update_inventory" },
            "admin" => new[]
            {
                "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order",
                "list_customers", "get_inventory", "update_inventory", "create_shipment", "update_shipment",
                "list_products", "list_suppliers"
            },
            _ => Array.Empty<string>()
        };

        if (!allowed.Contains(capability, StringComparer.Ordinal))
            throw new UnauthorizedAccessException($"Actor '{actor}' is not authorized for capability '{capability}'.");

        // Ownership check now applies to EVERY actor for EVERY customer-scoped
        // capability, not just "alice". Only "admin" may act across customers.
        if (customerId is not null
            && CustomerScopedCapabilities.Contains(capability)
            && !actor.Equals("admin", StringComparison.Ordinal))
        {
            if (!ActorCustomerMap.TryGetValue(actor, out var ownCustomerId) || ownCustomerId != customerId)
                throw new UnauthorizedAccessException("Actor is not authorized for the requested customer.");
        }
    }

    // Constant-time comparison so token checks don't leak length/prefix
    // information via response timing.
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}