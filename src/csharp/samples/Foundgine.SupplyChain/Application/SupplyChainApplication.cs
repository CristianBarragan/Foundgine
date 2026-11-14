using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Application.Querying;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Application;

/// <summary>
/// Capability facade. Every capability: authenticate + authorize the actor, then call Foundgine directly.
/// Reads are semantic lambdas (<see cref="SupplyChainReader"/> -> <see cref="FoundgineEfReader"/>: authorized plan ->
/// ExecutionIR -> EF Core). Writes call the generic repository (<see cref="IFoundgineRepository{T}"/>); the two
/// multi-step workflows live in <see cref="OrderWorkflows"/>; reference-data CRUD is in <c>Api/CrudEndpoints.cs</c>.
/// No DTOs, no mapping layer: the domain classes are the EF entities and result rows are returned as they come.
/// </summary>
public sealed class SupplyChainApplication(
    ICapabilityAuthorizer _auth,
    SupplyChainReader read,
    IFoundgineRepository<InventoryPosition> inventory,
    IFoundgineRepository<Shipment> shipments,
    OrderWorkflows orders)
{
    private static readonly string[] ShipmentStatuses = ["In Transit", "Out for Delivery", "Delivered", "Delayed"];

    public object DescribeCapabilities(string actor, string token)
    {
        _auth.Authenticate(actor, token); // introspection still needs valid credentials
        return new { actor, capabilities = _auth.Capabilities(actor) };
    }

    public async Task<object> GetMyOrders(string actor, string token, int customerId, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_my_orders", customerId);
        var r = await read.From<SalesOrder>().Eq(o => o.CustomerId, customerId).OrderBy(o => o.Id).ReadAsync(ct);
        return new { customerId, orders = r.Rows.Select(x => x.Values).ToArray(), plan = r.Fingerprint };
    }

    public async Task<object> GetOrder(string actor, string token, int customerId, int orderId, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_order", customerId);
        var r = await read.From<SalesOrder>()
            .Eq(o => o.Id, orderId).Eq(o => o.CustomerId, customerId)
            .Include(o => o.Lines)
            .ReadAsync(ct);
        var row = r.FirstOrThrow("Sales order not found.");
        return new { order = row.Values, lines = r.Rows.Select(x => x.Values).ToArray(), plan = r.Fingerprint };
    }

    public async Task<object> GetShipment(string actor, string token, int customerId, int shipmentId,
        CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_shipment", customerId);
        // Ownership is part of the query: the customer's orders that have this shipment.
        var owned = await read.From<SalesOrder>()
            .Eq(o => o.CustomerId, customerId)
            .Where(Filter<SalesOrder>.Some(o => o.Shipments, Filter<Shipment>.Eq(s => s.Id, shipmentId)))
            .Take(1).ReadAsync(ct);
        if (owned.Rows.Count == 0) throw new KeyNotFoundException("Shipment not found.");
        var r = await read.From<Shipment>().Eq(s => s.Id, shipmentId).ReadAsync(ct);
        return new { shipment = r.FirstOrThrow("Shipment not found.").Values, plan = r.Fingerprint };
    }

    public async Task<object> ListProducts(string actor, string token, CancellationToken ct)
    {
        _auth.Demand(actor, token, "list_products");
        return await List("products", read.From<CatalogProduct>().OrderBy(p => p.Id), ct);
    }

    public async Task<object> ListCustomers(string actor, string token, CancellationToken ct)
    {
        _auth.Demand(actor, token, "list_customers");
        return await List("customers", read.From<Customer>().OrderBy(c => c.Id), ct);
    }

    public async Task<object> ListSuppliers(string actor, string token, CancellationToken ct)
    {
        _auth.Demand(actor, token, "list_suppliers");
        return await List("suppliers", read.From<Supplier>().OrderBy(s => s.Id), ct);
    }

    public async Task<object> GetProduct(string actor, string token, int id, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_product");
        var r = await read.From<CatalogProduct>().Eq(p => p.Id, id).ReadAsync(ct);
        return new Dictionary<string, object?> { ["product"] = r.Rows.FirstOrDefault()?.Values, ["plan"] = r.Fingerprint };
    }

    public async Task<object> GetInventory(string actor, string token, int id, CancellationToken ct)
    {
        _auth.Demand(actor, token, "get_inventory");
        var r = await read.From<InventoryPosition>().Eq(i => i.ProductId, id).OrderBy(i => i.WarehouseId).ReadAsync(ct);
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

    private static async Task<object> List<T>(string name, Query<T> query, CancellationToken ct) where T : class
    {
        var r = await query.ReadAsync(ct);
        return new Dictionary<string, object?> { [name] = r.Rows.Select(x => x.Values).ToArray(), ["plan"] = r.Fingerprint };
    }
}
