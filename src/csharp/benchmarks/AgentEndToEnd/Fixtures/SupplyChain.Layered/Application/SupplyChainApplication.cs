using Foundgine.SupplyChain.Domain;

namespace Foundgine.SupplyChain.Application;

public sealed class SupplyChainApplication
{
    private readonly ICapabilityAuthorizer _auth;
    private readonly ISupplyChainQueries _queries;
    private readonly ISupplyChainMutations _mutations;

    public SupplyChainApplication(
        ICapabilityAuthorizer auth,
        ISupplyChainQueries queries,
        ISupplyChainMutations mutations)
    {
        _auth = auth;
        _queries = queries;
        _mutations = mutations;
    }

    public object DescribeCapabilities(string actor, string token)
    {
        // Capability discovery requires valid credentials, but does not
        // require a specific business capability beyond authentication.
        _auth.Authenticate(actor, token);

        return new
        {
            actor,
            capabilities = actor switch
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
            }
        };
    }

    public Task<object> GetMyOrders(
        string actor,
        string token,
        int customerId,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "get_my_orders",
            customerId);

        return _queries.GetOrders(
            customerId,
            ct);
    }

    public Task<object> GetOrder(
        string actor,
        string token,
        int customerId,
        int orderId,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "get_order",
            customerId);

        return _queries.GetOrder(
            customerId,
            orderId,
            ct);
    }

    public Task<object> GetShipment(
        string actor,
        string token,
        int customerId,
        int shipmentId,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "get_shipment",
            customerId);

        return _queries.GetShipment(
            customerId,
            shipmentId,
            ct);
    }

    public Task<object> ListProducts(
        string actor,
        string token,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "list_products");

        return _queries.ListProducts(ct);
    }

    public Task<object> ListCustomers(
        string actor,
        string token,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "list_customers");

        return _queries.ListCustomers(ct);
    }

    public Task<object> GetProduct(
        string actor,
        string token,
        int id,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "get_product");

        return _queries.GetProduct(
            id,
            ct);
    }

    public Task<object> GetInventory(
        string actor,
        string token,
        int id,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "get_inventory");

        return _queries.GetInventory(
            id,
            ct);
    }

    public Task<object> ListSuppliers(
        string actor,
        string token,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "list_suppliers");

        return _queries.ListSuppliers(ct);
    }

    public Task<object> UpdateInventory(
        string actor,
        string token,
        int warehouseId,
        int productId,
        int quantity,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "update_inventory");

        return _mutations.UpdateInventory(
            warehouseId,
            productId,
            quantity,
            ct);
    }

    public Task<object> CreateShipment(
        string actor,
        string token,
        int orderId,
        int carrierId,
        int warehouseId,
        string trackingNumber,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "create_shipment");

        return _mutations.CreateShipment(
            orderId,
            carrierId,
            warehouseId,
            trackingNumber,
            ct);
    }

    public Task<object> UpdateShipment(
        string actor,
        string token,
        int shipmentId,
        string status,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "update_shipment");

        return _mutations.UpdateShipment(
            shipmentId,
            status,
            ct);
    }

    public Task<object> PlaceOrder(
        string actor,
        string token,
        int customerId,
        OrderLine[] lines,
        string key,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "place_order",
            customerId);

        return _mutations.PlaceOrder(
            actor,
            customerId,
            lines,
            key,
            ct);
    }

    public Task<object> CancelOrder(
        string actor,
        string token,
        int customerId,
        int orderId,
        CancellationToken ct)
    {
        _auth.Demand(
            actor,
            token,
            "cancel_order",
            customerId);

        return _mutations.CancelOrder(
            actor,
            customerId,
            orderId,
            ct);
    }
}