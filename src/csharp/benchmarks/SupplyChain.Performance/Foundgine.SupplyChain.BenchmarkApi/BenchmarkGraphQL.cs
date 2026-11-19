using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Extensions.GraphQL.Http;
using HotChocolate.Language;

namespace Foundgine.SupplyChain.BenchmarkApi;

/// <summary>Benchmark GraphQL rules; the HTTP endpoint itself lives in Foundgine.Extensions.GraphQL.</summary>
public static class BenchmarkGraphQL
{
    public static void Configure(GraphQLHttpEndpointOptions o)
    {
        o.AllowMutations = true;

        // Foundgine grants customers Shipment.read so that their own orders can include shipments, but only SalesOrder has a
        // row-level predicate, so a customer must not be able to start a query at the Shipment root (that would expose every
        // customer's shipments). Mirrors the independent Hot Chocolate baseline.
        o.QueryRootRoles["salesOrder"] = ["sc-customer", "sc-operator", "sc-manager"];
        o.QueryRootRoles["catalogProduct"] = ["sc-customer", "sc-operator", "sc-analyst", "sc-manager"];
        o.QueryRootRoles["supplier"] = ["sc-analyst", "sc-manager"];
        o.QueryRootRoles["shipment"] = ["sc-operator", "sc-manager"];

        o.Authorize = Authorize;
    }

    // Match the independent Hot Chocolate baseline's customer boundary. The customer id is a validated Keycloak claim,
    // never a GraphQL argument.
    private static GraphQLAdapterError? Authorize(GraphQLHttpOperationContext context)
    {
        var principal = context.User;
        if (!principal.IsInRole("sc-customer")) return null;

        if (!int.TryParse(principal.FindFirst("customer_id")?.Value, out var customerId))
            return new GraphQLAdapterError("FORBIDDEN", "A customer_id claim is required for customer operations.", Category: "FORBIDDEN");

        if (context.Operation == OperationType.Mutation)
        {
            // Mutation batches are supported by the adapter. Keep customer
            // ownership validation complete by allowing only one order-create root.
            var rootFields = context.RootFields;
            if (rootFields.Count != 1 || rootFields[0].Name.Value != "createSalesOrder")
                return new GraphQLAdapterError("FORBIDDEN", "Customers may only submit one createSalesOrder operation per request.", Category: "FORBIDDEN");
            if (!context.Variables.TryGetValue("input", out var inputValue) ||
                inputValue is not IReadOnlyDictionary<string, object?> input ||
                !input.TryGetValue("customerId", out var requestedCustomer) ||
                !TryConvertInt32(requestedCustomer, out var requestedCustomerId) ||
                requestedCustomerId != customerId)
                return new GraphQLAdapterError("FORBIDDEN", "Customers may only create orders for their own customer_id.", Category: "FORBIDDEN");
        }

        return null;
    }

    private static bool TryConvertInt32(object? value, out int result)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }
}
