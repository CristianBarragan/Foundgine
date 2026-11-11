using System.Text.Json;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Advanced.OpenIntent.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace Foundgine.SupplyChain.Advanced.OpenIntent.Services;

public sealed record OrderLine(int ProductId, int Quantity);

public sealed class SupplyChainAuthorizer
{
    public bool CanExecute(string actor, string capability, int? requestedCustomerId = null,
        int? actorCustomerId = null)
    {
        if (actor == "admin") return true;
        if (actor == "bob")
            return capability is "get_my_orders" or "get_order" or "get_product" or "get_shipment" or "place_order"
                or "cancel_order" or "list_customers" or "find_top_supplier_overdue_orders";
        if (actor == "carol")
            return capability is "get_product" or "get_inventory" or "update_inventory" or "create_shipment"
                or "update_shipment";
        if (actor == "dave")
            return capability is "get_product" or "get_inventory" or "list_products" or "list_suppliers"
                or "update_inventory";
        if (actor == "alice")
        {
            if (capability is not ("get_my_orders" or "get_order" or "get_product" or "get_shipment" or "place_order"
                or "cancel_order")) return false;
            return requestedCustomerId is null || requestedCustomerId == 1;
        }

        if (actor.StartsWith("customer", StringComparison.OrdinalIgnoreCase))
        {
            if (capability is not ("get_my_orders" or "get_order" or "get_product" or "get_shipment" or "place_order"
                or "cancel_order")) return false;
            return requestedCustomerId is null || actorCustomerId == requestedCustomerId;
        }

        return false;
    }
}

/// <summary>
/// Service layer: everything that is neither transport (MCP) nor persistence. It owns actor authorization, error
/// shaping/logging, capability discovery and the generic entity operations, and delegates the complex queries and
/// mutations to <see cref="ISupplyChainRepository"/> (the preserved repository implementation).
/// </summary>
public sealed class SupplyChainService(
    ISupplyChainRepository repository,
    SupplyChainAuthorizer authorizer,
    FoundgineEntities entities,
    ILogger<SupplyChainService> logger)
{
    private static readonly string[] EntityCapabilities =
        ["list_entities", "read_entity", "get_entity", "create_entity", "update_entity", "delete_entity"];

    public object DescribeCapabilities(string actor) => new
    {
        actor,
        capabilities = actor switch
        {
            "alice" => new[]
                { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order" },
            "bob" => new[]
            {
                "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order",
                "list_customers", "find_top_supplier_overdue_orders"
            },
            "carol" => new[]
                { "get_product", "get_inventory", "update_inventory", "create_shipment", "update_shipment" },
            "dave" => new[] { "get_product", "get_inventory", "list_products", "list_suppliers", "update_inventory" },
            "admin" => new[]
            {
                "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order",
                "list_customers", "get_inventory", "update_inventory", "create_shipment", "update_shipment",
                "list_products", "list_suppliers", "find_top_supplier_overdue_orders"
            }.Concat(EntityCapabilities).ToArray(),
            _ => Array.Empty<string>()
        }
    };

    // ---- closed capability surface -------------------------------------------------------------------------

    public Task<object> GetMyOrders(string actor, int customerId, CancellationToken ct) =>
        Run("get_my_orders", actor, customerId, () => repository.GetOrders(customerId, ct));

    public Task<object> GetOrder(string actor, int customerId, int orderId, CancellationToken ct) =>
        Run("get_order", actor, customerId, () => repository.GetOrder(customerId, orderId, ct));

    public Task<object> GetShipment(string actor, int customerId, int shipmentId, CancellationToken ct) =>
        Run("get_shipment", actor, customerId, () => repository.GetShipment(customerId, shipmentId, ct));

    public Task<object> ListProducts(string actor, CancellationToken ct) =>
        Run("list_products", actor, null, () => repository.ListProducts(ct));

    public Task<object> ListCustomers(string actor, CancellationToken ct) =>
        Run("list_customers", actor, null, () => repository.ListCustomers(ct));

    public Task<object> GetProduct(string actor, int productId, CancellationToken ct) =>
        Run("get_product", actor, null, () => repository.GetProduct(productId, ct));

    public Task<object> GetInventory(string actor, int productId, CancellationToken ct) =>
        Run("get_inventory", actor, null, () => repository.GetInventory(productId, ct));

    public Task<object> ListSuppliers(string actor, CancellationToken ct) =>
        Run("list_suppliers", actor, null, () => repository.ListSuppliers(ct));

    // The ambiguity-resolution case from the Foundgine walkthrough: "top supplier in <state>" is not a database
    // key, so it is resolved through ranked candidates + evidence before anything is authorized to execute. See
    // SupplyChainExecutionService.FindTopSupplierOverdueOrders for all outcomes this can produce.
    public Task<object> FindTopSupplierOverdueOrders(string actor, string state, string? supplierName,
        CancellationToken ct) =>
        Run("find_top_supplier_overdue_orders", actor, null,
            () => repository.FindTopSupplierOverdueOrders(actor, state, supplierName, ct));

    public Task<object> UpdateInventory(string actor, int warehouseId, int productId, int quantity,
        CancellationToken ct) =>
        Run("update_inventory", actor, null, () => repository.UpdateInventory(warehouseId, productId, quantity, ct));

    public Task<object> CreateShipment(string actor, int orderId, int carrierId, int warehouseId,
        string trackingNumber, CancellationToken ct) =>
        Run("create_shipment", actor, null,
            () => repository.CreateShipment(orderId, carrierId, warehouseId, trackingNumber, ct));

    public Task<object> UpdateShipment(string actor, int shipmentId, string status, CancellationToken ct) =>
        Run("update_shipment", actor, null, () => repository.UpdateShipment(shipmentId, status, ct));

    public Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string idempotencyKey,
        CancellationToken ct) =>
        Run("place_order", actor, customerId,
            () => repository.PlaceOrder(actor, customerId, lines, idempotencyKey, ct));

    public Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct) =>
        Run("cancel_order", actor, customerId, () => repository.CancelOrder(actor, customerId, orderId, ct));

    // ---- generic entity surface (any entity of the model; admin only through the authorizer) ---------------

    public Task<object> ListEntities(string actor) =>
        Run("list_entities", actor, null, () => Task.FromResult<object>(new
        {
            entities = entities.Names.Select(n =>
            {
                var e = entities.Entity(n);
                return new { name = e.Name, fields = e.Fields };
            }).ToArray()
        }));

    public Task<object> ReadEntity(string actor, string entity, JsonElement? equals, string[]? fields,
        CancellationToken ct) =>
        Run("read_entity", actor, null, async () =>
        {
            var rows = await entities.Entity(entity).ReadAsync(ToValues(equals), fields, ct);
            return (object)new { entity, rows };
        });

    public Task<object> GetEntity(string actor, string entity, JsonElement id, CancellationToken ct) =>
        Run("get_entity", actor, null, async () =>
            (await entities.Entity(entity).GetAsync(id, ct))
            ?? throw new KeyNotFoundException($"{entity} {id} not found."));

    public Task<object> CreateEntity(string actor, string entity, JsonElement body, CancellationToken ct) =>
        Run("create_entity", actor, null, async () =>
            (object)new { entity, created = await entities.Entity(entity).CreateAsync(body, ct) });

    public Task<object> UpdateEntity(string actor, string entity, JsonElement id, JsonElement values,
        CancellationToken ct) =>
        Run("update_entity", actor, null, async () =>
            (object)new
            {
                entity,
                updated = await entities.Entity(entity).UpdateAsync(id, ToValues(values) ??
                    throw new ArgumentException("values are required."), ct)
            });

    public Task<object> DeleteEntity(string actor, string entity, JsonElement id, CancellationToken ct) =>
        Run("delete_entity", actor, null, async () =>
            (object)new { entity, deleted = await entities.Entity(entity).DeleteAsync(id, ct) });

    private static Dictionary<string, object?>? ToValues(JsonElement? json) =>
        json is { ValueKind: JsonValueKind.Object } o
            ? o.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal)
            : null;

    // ---- authorization + uniform error shaping ---------------------------------------------------------------

    private async Task<object> Run(string capability, string actor, int? requestedCustomerId,
        Func<Task<object>> operation)
    {
        // Every blocked or failed call gets a short opaque correlation id. The id itself carries no information an
        // attacker could use to distinguish "denied by authorization" from "not found" from "an unrelated bug" -
        // see BlockedCallClassification below and the "Why the execution API's error is uniform" section in
        // docs/SECURITY.md. The classification and full exception detail go only to the server-side log, keyed by
        // this id, so an operator can join a support ticket back to the exact log line without the client-facing
        // surface becoming a richer oracle.
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var actorCustomerId = ActorCustomerId(actor);

        if (!authorizer.CanExecute(actor, capability, requestedCustomerId, actorCustomerId))
        {
            LogBlockedCall(correlationId, BlockedCallClassification.AuthDenied, capability, actor,
                new UnauthorizedAccessException($"Actor '{actor}' is not authorized for capability '{capability}'."));
            throw BlockedCallError(capability, correlationId);
        }

        try
        {
            return await operation();
        }
        catch (McpException)
        {
            // Already an intentional, sanitized message a tool chose to surface on purpose: propagate as-is.
            throw;
        }
        catch (Exception ex)
        {
            LogBlockedCall(correlationId, Classify(ex), capability, actor, ex);
            throw BlockedCallError(capability, correlationId);
        }
    }

    /// <summary>
    /// Stable, greppable reasons a call can fail, independent of the
    /// specific .NET exception type. Kept separate from the exception type
    /// itself so the log query surface stays stable even if an internal
    /// implementation swaps which exception it throws for the same reason.
    /// </summary>
    private enum BlockedCallClassification
    {
        AuthDenied,
        NotFound,
        ValidationError,
        InvalidOperation,
        UnhandledException
    }

    private static BlockedCallClassification Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => BlockedCallClassification.AuthDenied,
        KeyNotFoundException => BlockedCallClassification.NotFound,
        ArgumentException => BlockedCallClassification.ValidationError,
        InvalidOperationException => BlockedCallClassification.InvalidOperation,
        _ => BlockedCallClassification.UnhandledException
    };

    private void LogBlockedCall(
        string correlationId, BlockedCallClassification classification, string capability, string actor,
        Exception ex)
    {
        // actor/capability/classification/correlationId are safe to log -
        // none of them are secrets and all of them are already known to the
        // caller who made the request. The full exception (ex.ToString() via
        // the logger's exception parameter) is what an operator needs to
        // tell "blocked by authorization" apart from "blocked by an
        // unrelated bug" without that distinction ever reaching the client.
        logger.LogWarning(ex,
            "Blocked/failed MCP call. correlationId={CorrelationId} classification={Classification} capability={Capability} actor={Actor}",
            correlationId, classification, capability, actor);
    }

    // McpException is the one exception type the SDK propagates verbatim to
    // the caller instead of collapsing into "An error occurred invoking
    // '<tool>'." (see McpServerImpl's default sanitization of non-McpException
    // failures). The message here is therefore deliberately uniform across
    // every BlockedCallClassification above - it must never vary by cause,
    // or it becomes exactly the oracle this design is meant to avoid.
    private static McpException BlockedCallError(string capability, string correlationId) =>
        new($"Request blocked while invoking '{capability}'. Reference: {correlationId}.");

    private static int ActorCustomerId(string actor)
    {
        if (actor.Equals("alice", StringComparison.OrdinalIgnoreCase)) return 1;
        if (actor.StartsWith("customer", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(actor[8..], out var id)) return id;
        return 0;
    }
}
