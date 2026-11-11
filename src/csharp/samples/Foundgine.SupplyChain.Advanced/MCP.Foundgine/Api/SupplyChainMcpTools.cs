using System.Text.Json;
using Foundgine.SupplyChain.Advanced.OpenIntent.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Foundgine.SupplyChain.Advanced.OpenIntent.Api;

/// <summary>
/// MCP transport only: one DI scope per call, then straight into <see cref="SupplyChainService"/>, which owns
/// authorization, rules and error shaping. The first block is the closed capability surface; the second block is
/// the generic entity surface (any entity of the model, admin only).
/// </summary>
[McpServerToolType]
public sealed class SupplyChainMcpTools(IServiceScopeFactory scopes)
{
    private async Task<object> With(Func<SupplyChainService, Task<object>> call)
    {
        using var scope = scopes.CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<SupplyChainService>());
    }

    [McpServerTool(Name = "describe_capabilities")]
    public Task<object> DescribeCapabilities(string actor) =>
        With(s => Task.FromResult(s.DescribeCapabilities(actor)));

    [McpServerTool(Name = "get_my_orders")]
    public Task<object> GetMyOrders(string actor, int customerId, CancellationToken ct = default) =>
        With(s => s.GetMyOrders(actor, customerId, ct));

    [McpServerTool(Name = "get_order")]
    public Task<object> GetOrder(string actor, int customerId, int orderId, CancellationToken ct = default) =>
        With(s => s.GetOrder(actor, customerId, orderId, ct));

    [McpServerTool(Name = "get_shipment")]
    public Task<object> GetShipment(string actor, int customerId, int shipmentId, CancellationToken ct = default) =>
        With(s => s.GetShipment(actor, customerId, shipmentId, ct));

    [McpServerTool(Name = "list_products")]
    public Task<object> ListProducts(string actor, CancellationToken ct = default) =>
        With(s => s.ListProducts(actor, ct));

    [McpServerTool(Name = "list_customers")]
    public Task<object> ListCustomers(string actor, CancellationToken ct = default) =>
        With(s => s.ListCustomers(actor, ct));

    [McpServerTool(Name = "get_product")]
    public Task<object> GetProduct(string actor, int productId, CancellationToken ct = default) =>
        With(s => s.GetProduct(actor, productId, ct));

    [McpServerTool(Name = "get_inventory")]
    public Task<object> GetInventory(string actor, int productId, CancellationToken ct = default) =>
        With(s => s.GetInventory(actor, productId, ct));

    [McpServerTool(Name = "list_suppliers")]
    public Task<object> ListSuppliers(string actor, CancellationToken ct = default) =>
        With(s => s.ListSuppliers(actor, ct));

    [McpServerTool(Name = "find_top_supplier_overdue_orders")]
    public Task<object> FindTopSupplierOverdueOrders(string actor, string state, string? supplierName = null,
        CancellationToken ct = default) =>
        With(s => s.FindTopSupplierOverdueOrders(actor, state, supplierName, ct));

    [McpServerTool(Name = "update_inventory")]
    public Task<object> UpdateInventory(string actor, int warehouseId, int productId, int quantity,
        CancellationToken ct = default) =>
        With(s => s.UpdateInventory(actor, warehouseId, productId, quantity, ct));

    [McpServerTool(Name = "create_shipment")]
    public Task<object> CreateShipment(string actor, int orderId, int carrierId, int warehouseId, string trackingNumber,
        CancellationToken ct = default) =>
        With(s => s.CreateShipment(actor, orderId, carrierId, warehouseId, trackingNumber, ct));

    [McpServerTool(Name = "update_shipment")]
    public Task<object> UpdateShipment(string actor, int shipmentId, string status, CancellationToken ct = default) =>
        With(s => s.UpdateShipment(actor, shipmentId, status, ct));

    [McpServerTool(Name = "place_order")]
    public Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string idempotencyKey,
        CancellationToken ct = default) =>
        With(s => s.PlaceOrder(actor, customerId, lines, idempotencyKey, ct));

    [McpServerTool(Name = "cancel_order")]
    public Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct = default) =>
        With(s => s.CancelOrder(actor, customerId, orderId, ct));

    // ---- generic entity surface (admin only) ---------------------------------------------------------------

    [McpServerTool(Name = "list_entities")]
    public Task<object> ListEntities(string actor) => With(s => s.ListEntities(actor));

    [McpServerTool(Name = "read_entity")]
    public Task<object> ReadEntity(string actor, string entity, JsonElement? equals = null, string[]? fields = null,
        CancellationToken ct = default) =>
        With(s => s.ReadEntity(actor, entity, equals, fields, ct));

    [McpServerTool(Name = "get_entity")]
    public Task<object> GetEntity(string actor, string entity, JsonElement id, CancellationToken ct = default) =>
        With(s => s.GetEntity(actor, entity, id, ct));

    [McpServerTool(Name = "create_entity")]
    public Task<object> CreateEntity(string actor, string entity, JsonElement body, CancellationToken ct = default) =>
        With(s => s.CreateEntity(actor, entity, body, ct));

    [McpServerTool(Name = "update_entity")]
    public Task<object> UpdateEntity(string actor, string entity, JsonElement id, JsonElement values,
        CancellationToken ct = default) =>
        With(s => s.UpdateEntity(actor, entity, id, values, ct));

    [McpServerTool(Name = "delete_entity")]
    public Task<object> DeleteEntity(string actor, string entity, JsonElement id, CancellationToken ct = default) =>
        With(s => s.DeleteEntity(actor, entity, id, ct));
}
