using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Extensions.GraphQL.Http;

namespace Foundgine.Shop;

/// <summary>Shop's GraphQL rules; the HTTP endpoint itself lives in Foundgine.Extensions.GraphQL.</summary>
public static class ShopGraphQL
{
    public static void Configure(GraphQLHttpEndpointOptions o)
    {
        // Customers hold OrderLine.read so their own orders can include lines, but only Customer and Order have
        // row-level predicates, so a customer must not START a query at the OrderLine root.
        o.QueryRootRoles["customer"] = ["manager", "customer"];
        o.QueryRootRoles["order"] = ["manager", "customer"];
        o.QueryRootRoles["product"] = ["manager", "customer"];
        o.QueryRootRoles["orderLine"] = ["manager"];

        // customer_id is a validated token claim, never a GraphQL argument.
        o.Authorize = ctx => ctx.User.IsInRole("customer") && !int.TryParse(ctx.User.FindFirst("customer_id")?.Value, out _)
            ? new GraphQLAdapterError("FORBIDDEN", "A customer_id claim is required for customer operations.", Category: "FORBIDDEN")
            : null;
    }
}
