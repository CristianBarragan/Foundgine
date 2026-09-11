using Foundgine.SupplyChain;
using Foundgine.SupplyChain.Application;
using ModelContextProtocol.Server;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Runtime;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// --- everything the old Application/DependencyInjection.cs + Infrastructure/DependencyInjection.cs
//     did, in one place, since there's only one project to wire up now. ---
builder.Services.AddFoundgine(options =>
{
    // Starter path: only the domain model + core Foundgine execution boundary.
    // Optional capabilities are deliberately opt-in.
    options.UseSupplyChainDomain();
});

builder.Services.AddFoundgineMcp();
builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<SupplyChainMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (NpgsqlDataSource ds, CancellationToken ct) =>
{
    await using var c = await ds.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand("SELECT 1", c);
    await cmd.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready" });
});
app.Run();

// Every tool requires a 'token' proving the caller actually controls the identity
// named by 'actor' - see Application/Authorization.cs.
[McpServerToolType]
public sealed class SupplyChainMcpTools
{
    private readonly IServiceScopeFactory _scopes;
    public SupplyChainMcpTools(IServiceScopeFactory scopes) => _scopes = scopes;

    private async Task<object> With(Func<SupplyChainApplication, Task<object>> f)
    {
        using var s = _scopes.CreateScope();
        return await f(s.ServiceProvider.GetRequiredService<SupplyChainApplication>());
    }

    [McpServerTool(Name = "describe_capabilities")]
    public Task<object> Describe(string actor, string token) =>
        With(a => Task.FromResult(a.DescribeCapabilities(actor, token)));

    [McpServerTool(Name = "get_my_orders")]
    public Task<object> GetMyOrders(string actor, string token, int customerId, CancellationToken ct = default) =>
        With(a => a.GetMyOrders(actor, token, customerId, ct));

    [McpServerTool(Name = "get_order")]
    public Task<object> GetOrder(string actor, string token, int customerId, int orderId,
        CancellationToken ct = default) => With(a => a.GetOrder(actor, token, customerId, orderId, ct));

    [McpServerTool(Name = "get_shipment")]
    public Task<object> GetShipment(string actor, string token, int customerId, int shipmentId,
        CancellationToken ct = default) => With(a => a.GetShipment(actor, token, customerId, shipmentId, ct));

    [McpServerTool(Name = "list_products")]
    public Task<object> ListProducts(string actor, string token, CancellationToken ct = default) =>
        With(a => a.ListProducts(actor, token, ct));

    [McpServerTool(Name = "list_customers")]
    public Task<object> ListCustomers(string actor, string token, CancellationToken ct = default) =>
        With(a => a.ListCustomers(actor, token, ct));

    [McpServerTool(Name = "get_product")]
    public Task<object> GetProduct(string actor, string token, int productId, CancellationToken ct = default) =>
        With(a => a.GetProduct(actor, token, productId, ct));

    [McpServerTool(Name = "get_inventory")]
    public Task<object> GetInventory(string actor, string token, int productId, CancellationToken ct = default) =>
        With(a => a.GetInventory(actor, token, productId, ct));

    [McpServerTool(Name = "list_suppliers")]
    public Task<object> ListSuppliers(string actor, string token, CancellationToken ct = default) =>
        With(a => a.ListSuppliers(actor, token, ct));

    [McpServerTool(Name = "update_inventory")]
    public Task<object> UpdateInventory(string actor, string token, int warehouseId, int productId, int quantity,
        CancellationToken ct = default) =>
        With(a => a.UpdateInventory(actor, token, warehouseId, productId, quantity, ct));

    [McpServerTool(Name = "create_shipment")]
    public Task<object> CreateShipment(string actor, string token, int orderId, int carrierId, int warehouseId,
        string trackingNumber, CancellationToken ct = default) => With(a =>
        a.CreateShipment(actor, token, orderId, carrierId, warehouseId, trackingNumber, ct));

    [McpServerTool(Name = "update_shipment")]
    public Task<object> UpdateShipment(string actor, string token, int shipmentId, string status,
        CancellationToken ct = default) => With(a => a.UpdateShipment(actor, token, shipmentId, status, ct));

    [McpServerTool(Name = "place_order")]
    public Task<object> PlaceOrder(string actor, string token, int customerId, OrderLine[] lines, string idempotencyKey,
        CancellationToken ct = default) => With(a => a.PlaceOrder(actor, token, customerId, lines, idempotencyKey, ct));

    [McpServerTool(Name = "cancel_order")]
    public Task<object> CancelOrder(string actor, string token, int customerId, int orderId,
        CancellationToken ct = default) => With(a => a.CancelOrder(actor, token, customerId, orderId, ct));
}