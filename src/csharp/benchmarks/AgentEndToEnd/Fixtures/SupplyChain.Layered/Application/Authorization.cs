using Foundgine.Core.Semantic.Authorization;

namespace Foundgine.SupplyChain.Application;

public sealed record CapabilityAuthorizationResult(
    string Actor,
    string Capability,
    int? CustomerId);

public interface ICapabilityAuthorizer
{
    CapabilityAuthorizationResult Demand(
        string actor,
        string token,
        string capability,
        int? customerId = null);

    void Authenticate(string actor, string token);
}

public sealed class SupplyChainAuthorizer : ICapabilityAuthorizer
{
    private static readonly Dictionary<string, string> ActorTokens =
        new(StringComparer.Ordinal)
        {
            ["alice"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_ALICE")
                        ?? "alice-demo-token",
            ["bob"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_BOB")
                      ?? "bob-demo-token",
            ["carol"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_CAROL")
                        ?? "carol-demo-token",
            ["dave"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_DAVE")
                       ?? "dave-demo-token",
            ["admin"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_ADMIN")
                        ?? "admin-demo-token",
        };

    private static readonly Dictionary<string, int> ActorCustomerMap =
        new(StringComparer.Ordinal)
        {
            ["alice"] = 1,
            ["bob"] = 2,
        };

    private static readonly HashSet<string> CustomerScopedCapabilities =
        new(StringComparer.Ordinal)
        {
            "get_my_orders",
            "get_order",
            "get_shipment",
            "place_order",
            "cancel_order"
        };

    public void Authenticate(string actor, string token)
    {
        if (string.IsNullOrEmpty(actor)
            || string.IsNullOrEmpty(token)
            || !ActorTokens.TryGetValue(actor, out var expectedToken)
            || !FixedTimeEquals(token, expectedToken))
        {
            throw new UnauthorizedAccessException("Invalid actor credentials.");
        }
    }

    public CapabilityAuthorizationResult Demand(
        string actor,
        string token,
        string capability,
        int? customerId = null)
    {
        Authenticate(actor, token);

        var allowed = actor switch
        {
            "alice" => new[]
            {
                "get_my_orders",
                "get_order",
                "get_product",
                "get_shipment",
                "place_order",
                "cancel_order"
            },

            "bob" => new[]
            {
                "get_my_orders",
                "get_order",
                "get_product",
                "get_shipment",
                "place_order",
                "cancel_order",
                "list_customers"
            },

            "carol" => new[]
            {
                "get_product",
                "get_inventory",
                "update_inventory",
                "create_shipment",
                "update_shipment"
            },

            "dave" => new[]
            {
                "get_product",
                "get_inventory",
                "list_products",
                "list_suppliers",
                "update_inventory"
            },

            "admin" => new[]
            {
                "get_my_orders",
                "get_order",
                "get_product",
                "get_shipment",
                "place_order",
                "cancel_order",
                "list_customers",
                "get_inventory",
                "update_inventory",
                "create_shipment",
                "update_shipment",
                "list_products",
                "list_suppliers"
            },

            _ => Array.Empty<string>()
        };

        if (!allowed.Contains(capability, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Actor '{actor}' is not authorized for capability '{capability}'.");
        }

        if (customerId is not null
            && CustomerScopedCapabilities.Contains(capability)
            && !actor.Equals("admin", StringComparison.Ordinal))
        {
            if (!ActorCustomerMap.TryGetValue(actor, out var ownCustomerId)
                || ownCustomerId != customerId)
            {
                throw new UnauthorizedAccessException(
                    "Actor is not authorized for the requested customer.");
            }
        }

        return new CapabilityAuthorizationResult(
            actor,
            capability,
            customerId);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var diff = 0;

        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];

        return diff == 0;
    }
}