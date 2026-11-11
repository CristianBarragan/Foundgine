using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Aot;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Domain;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Application;

/// <summary>
/// Capability facade. Every capability: authenticate + authorize the actor, then call Foundgine directly.
/// Reads are semantic operations (<see cref="FoundgineEfReader"/>: authorized plan -> ExecutionIR -> EF Core).
/// Writes call the generic repository (<see cref="IFoundgineRepository{T}"/>); the two multi-step workflows live
/// in <see cref="OrderWorkflows"/>. No DTOs, no mapping layer, no query abstraction: the domain classes are the
/// EF entities and the result rows are returned as they come.
/// </summary>
public sealed class SupplyChainApplication(
    ICapabilityAuthorizer _auth,
    FoundgineEfReader reader,
    IFoundgineRepository<InventoryPosition> inventory,
    IFoundgineRepository<Shipment> shipments,
    OrderWorkflows orders)
{
    private static readonly string[] ShipmentStatuses = ["In Transit", "Out for Delivery", "Delivered", "Delayed"];

    public object DescribeCapabilities(string actor, string token)
    {
        // describe_capabilities requires valid credentials too, but no
        // specific capability check beyond that - it's an introspection call.
        _auth.Authenticate(actor, token);
        return new
        {
            actor,
            capabilities = actor switch
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
                "dave" => new[]
                    { "get_product", "get_inventory", "list_products", "list_suppliers", "update_inventory" },
                "admin" => new[]
                {
                    "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order",
                    "list_customers", "get_inventory", "update_inventory", "create_shipment", "update_shipment",
                    "list_products", "list_suppliers"
                },
                _ => Array.Empty<string>()
            }
        };
    }

    public async Task<object> GetMyOrders(string actor, string token, int customerId, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_my_orders", customerId);
        var r = await reader.ReadAsync(SupplyChainSemantics.SalesOrder.Entity, SupplyChainSemantics.SalesOrder.All,
            SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId),
            [SupplyChainSemantics.SalesOrder.Id.Asc()], ct);
        return new { customerId, orders = r.Rows.Select(x => x.Values).ToArray(), plan = r.Fingerprint };
    }

    public async Task<object> GetOrder(string actor, string token, int customerId, int orderId, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_order", customerId);
        var r = await reader.ReadAsync(SupplyChainSemantics.SalesOrder.Entity, q => q
            .Where(SupplyChainSemantics.SalesOrder.Id.Eq(orderId),
                SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId))
            .Include(SupplyChainSemantics.SalesOrder.Relationships.Lines), ct);
        var row = r.FirstOrThrow("Sales order not found.");
        return new { order = row.Values, lines = r.Rows.Select(x => x.Values).ToArray(), plan = r.Fingerprint };
    }

    public async Task<object> GetShipment(string actor, string token, int customerId, int shipmentId,
        CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_shipment", customerId);
        // Ownership = read the shipment, then prove its order belongs to the customer (two authorized reads).
        var r = await reader.ReadAsync(SupplyChainSemantics.Shipment.Entity, SupplyChainSemantics.Shipment.All,
            SupplyChainSemantics.Shipment.Id.Eq(shipmentId), ct: ct);
        var row = r.Rows.FirstOrDefault() ?? throw new KeyNotFoundException("Shipment not found.");
        var owned = await reader.ReadAsync(SupplyChainSemantics.SalesOrder.Entity,
            [SupplyChainSemantics.SalesOrder.Id.Id],
            new SemanticAndFilter([
                SupplyChainSemantics.SalesOrder.Id.Eq(Convert.ToInt32(row.Values["OrderId"])),
                SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId)]), ct: ct);
        if (owned.Rows.Count == 0) throw new KeyNotFoundException("Shipment not found.");
        return new { shipment = row.Values, plan = r.Fingerprint };
    }

    public async Task<object> ListProducts(string actor, string token, CancellationToken ct)
    {
        _auth.Demand(actor, token, "list_products");
        return await List("products", SupplyChainSemantics.CatalogProduct.Entity,
            SupplyChainSemantics.CatalogProduct.All, ct);
    }

    public async Task<object> ListCustomers(string actor, string token, CancellationToken ct)
    {
        _auth.Demand(actor, token, "list_customers");
        return await List("customers", SupplyChainSemantics.Customer.Entity, SupplyChainSemantics.Customer.All, ct);
    }

    public async Task<object> ListSuppliers(string actor, string token, CancellationToken ct)
    {
        _auth.Demand(actor, token, "list_suppliers");
        return await List("suppliers", SupplyChainSemantics.Supplier.Entity, SupplyChainSemantics.Supplier.All, ct);
    }

    public async Task<object> GetProduct(string actor, string token, int id, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_product");
        var r = await reader.ReadAsync(SupplyChainSemantics.CatalogProduct.Entity,
            SupplyChainSemantics.CatalogProduct.All, SupplyChainSemantics.CatalogProduct.Id.Eq(id), ct: ct);
        return new Dictionary<string, object?> { ["product"] = r.Rows.FirstOrDefault()?.Values, ["plan"] = r.Fingerprint };
    }

    public async Task<object> GetInventory(string actor, string token, int id, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_inventory");
        var r = await reader.ReadAsync(SupplyChainSemantics.InventoryPosition.Entity,
            SupplyChainSemantics.InventoryPosition.All, SupplyChainSemantics.InventoryPosition.ProductId.Eq(id),
            [SupplyChainSemantics.InventoryPosition.WarehouseId.Asc()], ct);
        return new Dictionary<string, object?>
            { ["inventory"] = r.Rows.FirstOrDefault()?.Values, ["plan"] = r.Fingerprint, ["productId"] = id };
    }

    public async Task<object> UpdateInventory(string actor, string token, int w, int p, int q, CancellationToken ct)
    {
        _auth.Demand(actor, token, "update_inventory");
        if (q < 0) throw new ArgumentOutOfRangeException(nameof(q));
        var row = (await inventory.ListAsync(x => x.WarehouseId == w && x.ProductId == p, ct)).FirstOrDefault()
                  ?? throw new KeyNotFoundException("Inventory position not found.");
        var rows = await inventory.UpdateAsync(row.Id, x => x.QuantityOnHand, q, ct);
        return new { warehouseId = w, productId = p, quantity = q, result = new { rowsAffected = rows } };
    }

    public async Task<object> CreateShipment(string actor, string token, int o, int c, int w, string t,
        CancellationToken ct)
    {
        _auth.Demand(actor, token, "create_shipment");
        if (string.IsNullOrWhiteSpace(t)) throw new ArgumentException("Tracking number is required.");
        var s = await shipments.AddAsync(new Shipment
            { OrderId = o, CarrierId = c, WarehouseId = w, TrackingNumber = t, Status = "In Transit" }, ct);
        return new { shipment = new { s.Id, s.OrderId, s.CarrierId, s.WarehouseId, s.TrackingNumber, s.Status } };
    }

    public async Task<object> UpdateShipment(string actor, string token, int id, string status, CancellationToken ct)
    {
        _auth.Demand(actor, token, "update_shipment");
        if (!ShipmentStatuses.Contains(status, StringComparer.Ordinal))
            throw new InvalidOperationException("Invalid shipment status.");
        if (await shipments.UpdateAsync(id, x => x.Status, status, ct) == 0)
            throw new KeyNotFoundException("Shipment not found.");
        return new { shipment = new { Id = id, Status = status } };
    }

    public Task<object> PlaceOrder(string actor, string token, int customerId, OrderLine[] lines, string key,
        CancellationToken ct)
    {
        _auth.Demand(actor, token, "place_order", customerId);
        return orders.PlaceOrder(actor, customerId, lines, key, ct);
    }

    public Task<object> CancelOrder(string actor, string token, int customerId, int orderId, CancellationToken ct)
    {
        _auth.Demand(actor, token, "cancel_order", customerId);
        return orders.CancelOrder(customerId, orderId, ct);
    }

    private async Task<object> List(string name, EntityId entity, IReadOnlyList<FieldId> fields, CancellationToken ct)
    {
        var r = await reader.ReadAsync(entity, fields, ct: ct);
        return new Dictionary<string, object?> { [name] = r.Rows.Select(x => x.Values).ToArray(), ["plan"] = r.Fingerprint };
    }
}
