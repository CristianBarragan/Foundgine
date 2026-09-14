using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Runtime.Capabilities;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Runtime;
using Foundgine.SupplyChain.Advanced.OpenIntent;
using Foundgine.SupplyChain.Advanced.OpenIntent.Domain;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Planning;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration["SupplyChainConnectionString"]
         ?? Environment.GetEnvironmentVariable("SupplyChainConnectionString")
         ?? throw new InvalidOperationException("SupplyChainConnectionString is required.");

var semanticModel = OpenIntentSemanticModel.Model;
var dataSource = NpgsqlDataSource.Create(cs);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(semanticModel);
builder.Services.AddSingleton<ISemanticLexicalCandidateSource, SemanticContractLexicalCandidateSource>();
builder.Services.AddSingleton<IProviderPlanCompiler, SupplyChainSqlPlanCompiler>();
builder.Services.AddSingleton<IExecutionProvider, PooledSqlExecutionProvider>();

// The closed capability surface still uses its existing application-level
// authorizer. The open-intent engine has its own semantic policy below; keeping
// the two explicit prevents a transport adapter from silently becoming the
// application's authorization authority.
builder.Services.AddSingleton(new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()));
builder.Services.AddSingleton<SupplyChainAuthorizer>();
builder.Services.AddSingleton<Planner>();
builder.Services.AddScoped<SupplyChainExecutionService>();

// Foundgine's provider-neutral read pipeline: intent -> semantic graph ->
// authorization -> plan -> Execution IR -> SQL provider.
builder.Services.AddFoundgine(options =>
{
    options.Model = semanticModel;
    options.UseGrounding();
    options.AuthorizationPolicy = new AllowAllSemanticAuthorizationPolicy();
    options.WarrantKeyResolver = new OpenIntentDemoSecurity.KeyResolver();
    options.ExpectedWarrantIssuer = OpenIntentDemoSecurity.ExpectedIssuer;
    options.WarrantReplayStore = new MemorySecurityWarrantReplayStore();
});

// The MCP adapter itself is deliberately unaware of actor/token authentication.
// The host establishes SecurityExecutionContext before the adapter can execute.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISecurityExecutionContextProvider, HttpContextSecurityExecutionContextProvider>();
builder.Services.AddFoundgineMcp(() => new Foundgine.Core.Execution.ExecutionContext());

builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<SupplyChainMcpTools>()
    .WithTools<FoundgineMcpTools>();

var app = builder.Build();
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.Use(async (ctx, next) =>
    {
        await OpenIntentDemoSecurity.PopulateSecurityContextAsync(ctx);
        await next();
    }));
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

public sealed class PooledSqlExecutionProvider : IExecutionProvider
{
    private readonly NpgsqlDataSource _dataSource;

    public PooledSqlExecutionProvider(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        Foundgine.Core.Execution.ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await new SqlExecutionProvider(connection).ExecuteAsync(plan, context, cancellationToken);
    }
}

public sealed class HttpContextSecurityExecutionContextProvider : ISecurityExecutionContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextSecurityExecutionContextProvider(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public SecurityExecutionContext? GetSecurityExecutionContext() =>
        _httpContextAccessor.HttpContext is { } context
            ? OpenIntentDemoSecurity.GetSecurityExecutionContext(context)
            : null;
}

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

[McpServerToolType]
public sealed class SupplyChainMcpTools
{
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<SupplyChainMcpTools> logger;

    public SupplyChainMcpTools(IServiceScopeFactory scopes, ILogger<SupplyChainMcpTools> logger)
    {
        this.scopes = scopes;
        this.logger = logger;
    }

    [McpServerTool(Name = "describe_capabilities")]
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
            },
            _ => Array.Empty<string>()
        }
    };

    [McpServerTool(Name = "get_my_orders")]
    public Task<object> GetMyOrders(string actor, int customerId, CancellationToken ct = default) =>
        Execute("get_my_orders", actor, customerId, ct, (s, resolved) => s.GetOrders(resolved, ct));

    [McpServerTool(Name = "get_order")]
    public Task<object> GetOrder(string actor, int customerId, int orderId, CancellationToken ct = default) =>
        Execute("get_order", actor, customerId, ct, (s, _) => s.GetOrder(customerId, orderId, ct));

    [McpServerTool(Name = "get_shipment")]
    public Task<object> GetShipment(string actor, int customerId, int shipmentId, CancellationToken ct = default) =>
        Execute("get_shipment", actor, customerId, ct, (s, _) => s.GetShipment(customerId, shipmentId, ct));

    [McpServerTool(Name = "list_products")]
    public Task<object> ListProducts(string actor, CancellationToken ct = default) =>
        Execute("list_products", actor, null, ct, (s, _) => s.ListProducts(ct));

    [McpServerTool(Name = "list_customers")]
    public Task<object> ListCustomers(string actor, CancellationToken ct = default) =>
        Execute("list_customers", actor, null, ct, (s, _) => s.ListCustomers(ct));

    [McpServerTool(Name = "get_product")]
    public Task<object> GetProduct(string actor, int productId, CancellationToken ct = default) =>
        Execute("get_product", actor, null, ct, (s, _) => s.GetProduct(productId, ct));

    [McpServerTool(Name = "get_inventory")]
    public Task<object> GetInventory(string actor, int productId, CancellationToken ct = default) =>
        Execute("get_inventory", actor, null, ct, (s, _) => s.GetInventory(productId, ct));

    [McpServerTool(Name = "list_suppliers")]
    public Task<object> ListSuppliers(string actor, CancellationToken ct = default) =>
        Execute("list_suppliers", actor, null, ct, (s, _) => s.ListSuppliers(ct));

    // The ambiguity-resolution case from the Foundgine walkthrough
    // (docs-site/walkthrough/index.html): "top supplier in <state>" is not a
    // database key, so it is resolved through ranked candidates + evidence
    // before anything is authorized to execute. supplierName is optional -
    // when the caller (agent) has already been told candidates are tied and
    // comes back with a specific name, that closes the loop into a resolved
    // result instead of asking again. See
    // SupplyChainExecutionService.FindTopSupplierOverdueOrders for all
    // outcomes this can produce.
    [McpServerTool(Name = "find_top_supplier_overdue_orders")]
    public Task<object> FindTopSupplierOverdueOrders(string actor, string state, string? supplierName = null,
        CancellationToken ct = default) =>
        Execute("find_top_supplier_overdue_orders", actor, null, ct,
            (s, _) => s.FindTopSupplierOverdueOrders(actor, state, supplierName, ct));

    [McpServerTool(Name = "update_inventory")]
    public Task<object> UpdateInventory(string actor, int warehouseId, int productId, int quantity,
        CancellationToken ct = default) =>
        Execute("update_inventory", actor, null, ct, (s, _) => s.UpdateInventory(warehouseId, productId, quantity, ct));

    [McpServerTool(Name = "create_shipment")]
    public Task<object> CreateShipment(string actor, int orderId, int carrierId, int warehouseId, string trackingNumber,
        CancellationToken ct = default) =>
        Execute("create_shipment", actor, null, ct,
            (s, _) => s.CreateShipment(orderId, carrierId, warehouseId, trackingNumber, ct));

    [McpServerTool(Name = "update_shipment")]
    public Task<object> UpdateShipment(string actor, int shipmentId, string status, CancellationToken ct = default) =>
        Execute("update_shipment", actor, null, ct, (s, _) => s.UpdateShipment(shipmentId, status, ct));

    [McpServerTool(Name = "place_order")]
    public Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string idempotencyKey,
        CancellationToken ct = default) =>
        Execute("place_order", actor, customerId, ct,
            (s, _) => s.PlaceOrder(actor, customerId, lines, idempotencyKey, ct));

    [McpServerTool(Name = "cancel_order")]
    public Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct = default) =>
        Execute("cancel_order", actor, customerId, ct, (s, _) => s.CancelOrder(actor, customerId, orderId, ct));

    private async Task<object> Execute(
        string capability,
        string actor,
        int? requestedCustomerId,
        CancellationToken ct,
        Func<SupplyChainExecutionService, int, Task<object>> operation)
    {
        // Every blocked or failed call gets a short opaque correlation id.
        // The id itself carries no information an attacker could use to
        // distinguish "denied by authorization" from "not found" from "an
        // unrelated bug" - see BlockedCallClassification below and the
        // "Why the execution API's error is uniform" section in
        // docs/SECURITY.md for the reasoning. The classification and full
        // exception detail go only to the server-side log, keyed by this id,
        // so an operator can join a support ticket or alert back to the
        // exact log line without the client-facing surface becoming a richer
        // oracle than it is today.
        var correlationId = Guid.NewGuid().ToString("N")[..8];

        using var scope = scopes.CreateScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<SupplyChainAuthorizer>();
        var service = scope.ServiceProvider.GetRequiredService<SupplyChainExecutionService>();
        var actorCustomerId = ActorCustomerId(actor);

        if (!authorizer.CanExecute(actor, capability, requestedCustomerId, actorCustomerId))
        {
            LogBlockedCall(correlationId, BlockedCallClassification.AuthDenied, capability, actor,
                new UnauthorizedAccessException($"Actor '{actor}' is not authorized for capability '{capability}'."));
            throw BlockedCallError(capability, correlationId);
        }

        try
        {
            return await operation(service, requestedCustomerId ?? 0);
        }
        catch (McpException)
        {
            // Already an intentional, sanitized message a tool chose to
            // surface on purpose (see ModelContextProtocol.McpException) -
            // propagate it as-is rather than double-wrapping it.
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

public sealed class SupplyChainExecutionService
{
    private readonly NpgsqlDataSource ds;
    private readonly Planner planner;
    private readonly SemanticAuthorizer authorizer;
    private readonly SemanticContractSnapshot contract;

    public SupplyChainExecutionService(
        NpgsqlDataSource ds,
        Planner planner,
        SemanticAuthorizer authorizer,
        SemanticContractSnapshot contract)
    {
        this.ds = ds;
        this.planner = planner;
        this.authorizer = authorizer;
        this.contract = contract;
    }

    public async Task<object> GetOrders(int customerId, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand(
                "SELECT order_id, customer_id, status, total_amount, order_date FROM orders WHERE customer_id=@c ORDER BY order_id",
                c);
        cmd.Parameters.AddWithValue("c", customerId);
        var orders = await ReadRows(cmd, ct);
        return new { customerId, orders, plan = PlanFingerprint(PlanCustomerOrders()) };
    }

    public async Task<object> GetOrder(int customerId, int orderId, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand(
                "SELECT order_id, customer_id, status, total_amount, order_date FROM orders WHERE order_id=@o AND customer_id=@c",
                c);
        cmd.Parameters.AddWithValue("o", orderId);
        cmd.Parameters.AddWithValue("c", customerId);
        var order = await ReadSingle(cmd, ct);
        if (order is null) throw new KeyNotFoundException("Order not found.");

        await using var items =
            new NpgsqlCommand(
                "SELECT oi.order_item_id, oi.product_id, p.product_name, p.sku, oi.quantity, oi.unit_price FROM order_items oi JOIN products p ON p.product_id=oi.product_id WHERE oi.order_id=@o ORDER BY oi.order_item_id",
                c);
        items.Parameters.AddWithValue("o", orderId);
        var itemRows = await ReadRows(items, ct);
        return new { order, items = itemRows, plan = PlanFingerprint(PlanCustomerOrders()) };
    }

    public async Task<object> GetShipment(int customerId, int shipmentId, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        const string sql = """
                           SELECT s.shipment_id, s.order_id, s.carrier_id, ca.carrier_name,
                                  s.warehouse_id, w.warehouse_name, s.tracking_number,
                                  s.shipping_status
                           FROM shipments s
                           JOIN orders o ON o.order_id=s.order_id
                           LEFT JOIN carriers ca ON ca.carrier_id=s.carrier_id
                           LEFT JOIN warehouses w ON w.warehouse_id=s.warehouse_id
                           WHERE s.shipment_id=@s AND o.customer_id=@c
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("s", shipmentId);
        cmd.Parameters.AddWithValue("c", customerId);
        var shipment = await ReadSingle(cmd, ct);
        if (shipment is null) throw new KeyNotFoundException("Shipment not found.");
        return new { shipment };
    }

    public async Task<object> ListProducts(CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand("SELECT product_id, product_name, sku, unit_price FROM products ORDER BY product_id", c);
        return new { products = await ReadRows(cmd, ct), plan = PlanFingerprint(PlanProduct()) };
    }

    public async Task<object> ListCustomers(CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand("SELECT customer_id, first_name, last_name, email FROM customers ORDER BY customer_id",
                c);
        return new { customers = await ReadRows(cmd, ct) };
    }

    public async Task<object> GetProduct(int productId, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        const string sql = """
                           SELECT p.product_id, p.product_name, p.sku, p.unit_price,
                                  s.supplier_id, s.supplier_name, s.email AS supplier_email,
                                  ca.category_id, ca.category_name
                           FROM products p
                           LEFT JOIN suppliers s ON s.supplier_id=p.supplier_id
                           LEFT JOIN categories ca ON ca.category_id=p.category_id
                           WHERE p.product_id=@p
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("p", productId);
        var product = await ReadSingle(cmd, ct);
        if (product is null) throw new KeyNotFoundException("Product not found.");
        return new { product, plan = PlanFingerprint(PlanProduct()) };
    }

    public async Task<object> GetInventory(int productId, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        const string sql = """
                           SELECT i.inventory_id, i.warehouse_id, w.warehouse_name,
                                  i.product_id, i.quantity_on_hand, i.reorder_level
                           FROM inventory i
                           JOIN warehouses w ON w.warehouse_id=i.warehouse_id
                           WHERE i.product_id=@p ORDER BY i.warehouse_id
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("p", productId);
        return new { productId, inventory = await ReadRows(cmd, ct) };
    }

    public async Task<object> ListSuppliers(CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand("SELECT supplier_id, supplier_name, email FROM suppliers ORDER BY supplier_id", c);
        return new { suppliers = await ReadRows(cmd, ct) };
    }

    // Mirrors docs-site/walkthrough/index.html: "top supplier in <state>" is
    // language, not a database key, so it goes through retrieval (ranked
    // candidates + evidence) before anything downstream may execute.
    //
    // Outcomes, none of which are errors:
    //   - "not_found": no supplier exists for the state at all, or a
    //     supplierName was given that doesn't match any candidate exactly
    //     AND doesn't turn up anything via approximate retrieval either
    //     (see TryApproximateSupplierMatchAsync). Nothing to resolve, so
    //     nothing is authorized or executed.
    //   - "clarification_needed": either two or more suppliers are tied for
    //     "top" (evidence.strategy = "relational"), or a supplierName that
    //     missed an exact match turned up look-alikes via Fuzzy/FullText/
    //     Search retrieval (evidence.strategy = "fuzzy"/"fulltext"/
    //     "search") - a "did you mean" rather than a tie. Either way,
    //     resolution stops here. It does not guess, does not authorize, and
    //     does not execute the purchase-order query - it hands the ranked
    //     candidates back to the caller and asks for a more specific intent
    //     (a supplier name, a tiebreak criterion, a narrower region).
    //   - "resolved": exactly one candidate ranks highest for the state, OR
    //     the caller closed the loop by naming a specific supplier
    //     (supplierName) after a prior clarification_needed response. The
    //     graph is bound to that real supplier, treated as authorized (see
    //     the Plan(...) comment on why authorization here is an allow-all
    //     provenance stamp rather than a second access-control layer) and
    //     the overdue purchase orders are executed and returned with
    //     evidence. Supplier.NegotiatedCost is a commercially sensitive
    //     field - like Supplier.NegotiatedCost in the walkthrough, it is
    //     stripped from the response for every actor except admin, and
    //     listed under deniedFields.
    public async Task<object> FindTopSupplierOverdueOrders(string actor, string state, string? supplierName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required.");

        await using var c = await ds.OpenConnectionAsync(ct);

        // Retrieval: ranked candidates + provenance. This step alone cannot
        // grant access to anything.
        const string candidateSql = """
                                    SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost
                                    FROM suppliers
                                    WHERE state = @state
                                    ORDER BY total_order_value DESC, supplier_id
                                    """;
        await using var candidateCmd = new NpgsqlCommand(candidateSql, c);
        candidateCmd.Parameters.AddWithValue("state", state.ToUpperInvariant());
        var candidates = await ReadRows(candidateCmd, ct);

        if (candidates.Count == 0)
        {
            return new { status = "not_found", state, reason = $"No suppliers found in state '{state}'." };
        }

        Dictionary<string, object?> supplier;

        if (!string.IsNullOrWhiteSpace(supplierName))
        {
            // The caller is closing the loop: a prior call told them the
            // candidates were tied, and they came back with a specific
            // name instead of leaving Foundgine to guess. Still validated
            // against the real candidate set for the state - a name that
            // doesn't match anything in scope is refused, not guessed at.
            var named = candidates.SingleOrDefault(x =>
                string.Equals((string)x["supplier_name"]!, supplierName, StringComparison.OrdinalIgnoreCase));

            if (named is null)
            {
                // Exact match failed. Before answering not_found, ask
                // whether the name is only slightly off - a typo, different
                // word order, a partial phrase - via the same approximate
                // retrieval strategies PostgresRetrievalCandidateSource
                // exposes as RetrievalStrategy.Fuzzy/FullText/Search. A hit
                // here is evidence for a "did you mean" clarification, not
                // an auto-resolve: it still hands candidates back to the
                // caller instead of binding the graph to a guess.
                var approx = await TryApproximateSupplierMatchAsync(c, state, supplierName, ct);

                if (approx is { Matches.Count: > 0 })
                {
                    return new
                    {
                        status = "clarification_needed",
                        state,
                        reason = $"'{supplierName}' does not exactly match any supplier in state '{state}', " +
                                 $"but {approx.Value.Matches.Count} looked similar via {approx.Value.Strategy} retrieval.",
                        candidates = approx.Value.Matches.Select(x => new
                        {
                            id = x["supplier_id"],
                            name = x["supplier_name"],
                            state = x["state"],
                            totalOrderValue = x["total_order_value"],
                            score = x["score"]
                        }),
                        evidence = new { strategy = approx.Value.Strategy, matchedAgainst = supplierName, tie = false },
                        suggestedRefinements = new[]
                        {
                            "Re-call with the exact supplierName from the candidates above.",
                            "Narrow to a more specific region than the state."
                        }
                    };
                }

                return new
                {
                    status = "not_found",
                    state,
                    reason = $"'{supplierName}' does not match any supplier in state '{state}'.",
                    strategiesTried = PgSearchEnabled
                        ? new[] { "exact", "fuzzy", "fulltext", "search" }
                        : new[] { "exact", "fuzzy", "fulltext" },
                    candidates = candidates.Select(x => new
                    {
                        id = x["supplier_id"], name = x["supplier_name"], state = x["state"],
                        totalOrderValue = x["total_order_value"]
                    })
                };
            }

            supplier = named;
        }
        else
        {
            var topValue = (decimal)candidates[0]["total_order_value"]!;
            var tiedAtTop = candidates.Where(x => (decimal)x["total_order_value"]! == topValue).ToList();

            if (tiedAtTop.Count > 1)
            {
                return new
                {
                    status = "clarification_needed",
                    state,
                    reason =
                        $"{tiedAtTop.Count} suppliers are tied for 'top' by total order value in state '{state}'; the request cannot be resolved to one supplier without more specific intent.",
                    candidates = tiedAtTop.Select(x => new
                    {
                        id = x["supplier_id"], name = x["supplier_name"], state = x["state"],
                        totalOrderValue = x["total_order_value"]
                    }),
                    evidence = new { strategy = "relational", orderBy = "total_order_value desc", tie = true },
                    suggestedRefinements = new[]
                    {
                        "Name the supplier directly (pass supplierName on the next call).",
                        "Give a tiebreak criterion (for example: most recent purchase order, or lowest lead time).",
                        "Narrow to a more specific region than the state."
                    }
                };
            }

            supplier = candidates[0];
        }

        var supplierId = (int)supplier["supplier_id"]!;
        var supplierValue = (decimal)supplier["total_order_value"]!;
        var runnerUp = candidates.Where(x => (int)x["supplier_id"]! != supplierId).ToList();
        var runnerUpValue = runnerUp.Count > 0 ? runnerUp.Max(x => (decimal)x["total_order_value"]!) : 0m;

        // Resolution + authorization + plan binding: the graph is finalized
        // against the one real supplier the candidate resolved to. See the
        // Plan(...) comment for what "authorization" means in this fixed
        // benchmark schema.
        var plan = PlanSupplier();

        const string poSql = """
                             SELECT purchase_order_id, expected_date
                             FROM purchase_orders
                             WHERE supplier_id = @supplierId
                               AND received_date IS NULL
                               AND expected_date < CURRENT_DATE
                             ORDER BY expected_date
                             """;
        await using var poCmd = new NpgsqlCommand(poSql, c);
        poCmd.Parameters.AddWithValue("supplierId", supplierId);
        var overdueRows = await ReadRows(poCmd, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overduePurchaseOrders = overdueRows.Select(r =>
        {
            var expected = DateOnly.FromDateTime((DateTime)r["expected_date"]!);
            return new
            {
                purchaseOrderId = r["purchase_order_id"],
                expectedDate = expected,
                daysLate = today.DayNumber - expected.DayNumber
            };
        }).ToList();

        // Field-level authorization, mirroring step 7 of the walkthrough:
        // NegotiatedCost is denied to every actor except admin, regardless
        // of the fact that the capability call itself was allowed.
        var isAdmin = string.Equals(actor, "admin", StringComparison.OrdinalIgnoreCase);
        var deniedFields = isAdmin ? Array.Empty<string>() : new[] { "Supplier.NegotiatedCost" };

        return new
        {
            status = "resolved",
            state,
            resolvedBy = string.IsNullOrWhiteSpace(supplierName) ? "ranking" : "explicit-name",
            supplier = new
            {
                id = supplierId,
                name = supplier["supplier_name"],
                state = supplier["state"],
                totalOrderValue = supplierValue,
                negotiatedCost = isAdmin ? supplier["negotiated_cost"] : null
            },
            evidence = new
            {
                strategy = "relational",
                orderBy = "total_order_value desc",
                rank = 1,
                marginOverRunnerUp = supplierValue - runnerUpValue
            },
            authorization = new { decision = "allow", deniedFields },
            overduePurchaseOrders,
            rowCount = overduePurchaseOrders.Count,
            plan = PlanFingerprint(plan)
        };
    }

    public async Task<object> UpdateInventory(int warehouseId, int productId, int quantity, CancellationToken ct)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand(
                "UPDATE inventory SET quantity_on_hand=@q,last_updated=CURRENT_TIMESTAMP WHERE warehouse_id=@w AND product_id=@p RETURNING inventory_id,quantity_on_hand",
                c);
        cmd.Parameters.AddWithValue("q", quantity);
        cmd.Parameters.AddWithValue("w", warehouseId);
        cmd.Parameters.AddWithValue("p", productId);
        var row = await ReadSingle(cmd, ct);
        if (row is null) throw new KeyNotFoundException("Inventory row not found.");
        return new { warehouseId, productId, quantity, result = row };
    }

    public async Task<object> CreateShipment(int orderId, int carrierId, int warehouseId, string trackingNumber,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber)) throw new ArgumentException("Tracking number is required.");
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO shipments(order_id,carrier_id,warehouse_id,tracking_number,shipping_status) VALUES(@o,@c,@w,@t,'In Transit') RETURNING shipment_id,order_id,carrier_id,warehouse_id,tracking_number,shipping_status",
            c);
        cmd.Parameters.AddWithValue("o", orderId);
        cmd.Parameters.AddWithValue("c", carrierId);
        cmd.Parameters.AddWithValue("w", warehouseId);
        cmd.Parameters.AddWithValue("t", trackingNumber);
        var shipment = await ReadSingle(cmd, ct);
        return new { shipment };
    }

    public async Task<object> UpdateShipment(int shipmentId, string status, CancellationToken ct)
    {
        var allowed = new[] { "In Transit", "Out for Delivery", "Delivered", "Delayed" };
        if (!allowed.Contains(status, StringComparer.Ordinal))
            throw new InvalidOperationException("Invalid shipment status.");
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var cmd =
            new NpgsqlCommand(
                "UPDATE shipments SET shipping_status=@s WHERE shipment_id=@i RETURNING shipment_id,shipping_status",
                c);
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("i", shipmentId);
        var shipment = await ReadSingle(cmd, ct);
        if (shipment is null) throw new KeyNotFoundException("Shipment not found.");
        return new { shipment };
    }

    public async Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string key,
        CancellationToken ct)
    {
        if (lines is null || lines.Length == 0) throw new ArgumentException("At least one line is required.");
        if (lines.Any(x => x.Quantity <= 0)) throw new InvalidOperationException("Quantity must be positive.");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Idempotency key is required.");

        var plan = PlanPlaceOrder();
        var requested = lines.GroupBy(x => x.ProductId).Select(g => new OrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToArray();

        await using var c = await ds.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k));", c, tx))
        {
            lockCmd.Parameters.AddWithValue("k", key);
            await lockCmd.ExecuteScalarAsync(ct);
        }

        await using (var existing =
                     new NpgsqlCommand(
                         "SELECT order_id FROM supply_chain_idempotency WHERE idempotency_key=@k FOR SHARE", c, tx))
        {
            existing.Parameters.AddWithValue("k", key);
            var v = await existing.ExecuteScalarAsync(ct);
            if (v is not null)
            {
                await tx.CommitAsync(ct);
                return new { orderId = Convert.ToInt32(v), replay = true, plan = PlanFingerprint(plan) };
            }
        }

        await using (var owner = new NpgsqlCommand("SELECT customer_id FROM customers WHERE customer_id=@id", c, tx))
        {
            owner.Parameters.AddWithValue("id", customerId);
            if (await owner.ExecuteScalarAsync(ct) is null) throw new InvalidOperationException("Customer not found.");
        }

        decimal total = 0;
        var resolved = new List<(int product, int qty, decimal price, int warehouse)>();
        foreach (var line in requested)
        {
            await using var p = new NpgsqlCommand("SELECT unit_price FROM products WHERE product_id=@p", c, tx);
            p.Parameters.AddWithValue("p", line.ProductId);
            var v = await p.ExecuteScalarAsync(ct);
            if (v is null) throw new InvalidOperationException($"Product {line.ProductId} not found.");
            var price = (decimal)v;

            await using var stock =
                new NpgsqlCommand(
                    "SELECT warehouse_id FROM inventory WHERE product_id=@p AND quantity_on_hand>=@q ORDER BY quantity_on_hand DESC, warehouse_id FOR UPDATE SKIP LOCKED LIMIT 1",
                    c, tx);
            stock.Parameters.AddWithValue("p", line.ProductId);
            stock.Parameters.AddWithValue("q", line.Quantity);
            var w = await stock.ExecuteScalarAsync(ct);
            if (w is null) throw new InvalidOperationException($"Insufficient inventory for product {line.ProductId}.");
            var warehouse = Convert.ToInt32(w);
            resolved.Add((line.ProductId, line.Quantity, price, warehouse));
            total += price * line.Quantity;
        }

        int orderId;
        await using (var ins = new NpgsqlCommand(
                         "INSERT INTO orders(customer_id,status,total_amount) VALUES(@c,'Pending',@t) RETURNING order_id",
                         c, tx))
        {
            ins.Parameters.AddWithValue("c", customerId);
            ins.Parameters.AddWithValue("t", total);
            orderId = Convert.ToInt32(await ins.ExecuteScalarAsync(ct));
        }

        foreach (var x in resolved)
        {
            int itemId;
            await using (var oi = new NpgsqlCommand(
                             "INSERT INTO order_items(order_id,product_id,quantity,unit_price) VALUES(@o,@p,@q,@u) RETURNING order_item_id",
                             c, tx))
            {
                oi.Parameters.AddWithValue("o", orderId);
                oi.Parameters.AddWithValue("p", x.product);
                oi.Parameters.AddWithValue("q", x.qty);
                oi.Parameters.AddWithValue("u", x.price);
                itemId = Convert.ToInt32(await oi.ExecuteScalarAsync(ct));
            }

            await using var alloc = new NpgsqlCommand(
                "INSERT INTO order_allocations(order_item_id,warehouse_id,quantity) VALUES(@i,@w,@q); UPDATE inventory SET quantity_on_hand=quantity_on_hand-@q,last_updated=CURRENT_TIMESTAMP WHERE warehouse_id=@w AND product_id=@p AND quantity_on_hand>=@q",
                c, tx);
            alloc.Parameters.AddWithValue("i", itemId);
            alloc.Parameters.AddWithValue("w", x.warehouse);
            alloc.Parameters.AddWithValue("q", x.qty);
            alloc.Parameters.AddWithValue("p", x.product);
            var affected = await alloc.ExecuteNonQueryAsync(ct);
            if (affected < 2)
                throw new InvalidOperationException("Inventory changed before reservation could be committed.");
        }

        await using (var idem = new NpgsqlCommand(
                         "INSERT INTO supply_chain_idempotency(idempotency_key,actor_id,operation,order_id) VALUES(@k,@a,'place_order',@o)",
                         c, tx))
        {
            idem.Parameters.AddWithValue("k", key);
            idem.Parameters.AddWithValue("a", ActorNumber(actor));
            idem.Parameters.AddWithValue("o", orderId);
            await idem.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new
        {
            orderId, replay = false, total, plan = PlanFingerprint(plan),
            evidence = Hash($"place_order|{actor}|{customerId}|{orderId}|{key}")
        };
    }

    public async Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        await using (var q = new NpgsqlCommand(
                         "UPDATE orders SET status='Cancelled' WHERE order_id=@o AND customer_id=@c AND status='Pending' RETURNING order_id",
                         c, tx))
        {
            q.Parameters.AddWithValue("o", orderId);
            q.Parameters.AddWithValue("c", customerId);
            if (await q.ExecuteScalarAsync(ct) is null)
                throw new UnauthorizedAccessException("Order is not owned by the customer or is not cancellable.");
        }

        await using (var items = new NpgsqlCommand(
                         "UPDATE inventory i SET quantity_on_hand=i.quantity_on_hand+a.quantity,last_updated=CURRENT_TIMESTAMP FROM order_allocations a JOIN order_items oi ON oi.order_item_id=a.order_item_id WHERE oi.order_id=@o AND i.warehouse_id=a.warehouse_id AND i.product_id=oi.product_id",
                         c, tx))
        {
            items.Parameters.AddWithValue("o", orderId);
            await items.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new { orderId, status = "Cancelled", restoredInventory = true };
    }

    private SemanticPlan PlanCustomerOrders() => Plan(new SemanticOperation(
        new SemanticReadNode(1, OpenIntentSemanticIds.Entity("Customer"),
            new[] { OpenIntentSemanticIds.Field("Customer", "Id"), OpenIntentSemanticIds.Field("Customer", "FirstName"), OpenIntentSemanticIds.Field("Customer", "LastName") }, null, null,
            new[]
            {
                new SemanticReadNode(2, OpenIntentSemanticIds.Entity("Order"),
                    new[] { OpenIntentSemanticIds.Field("Order", "Id"), OpenIntentSemanticIds.Field("Order", "Status"), OpenIntentSemanticIds.Field("Order", "TotalAmount") },
                    OpenIntentSemanticIds.Relationship("Customer", "orders"), null, Array.Empty<SemanticReadNode>())
            })));

    private SemanticPlan PlanProduct() => Plan(new SemanticOperation(
        new SemanticReadNode(1, OpenIntentSemanticIds.Entity("Product"),
            new[] { OpenIntentSemanticIds.Field("Product", "Id"), OpenIntentSemanticIds.Field("Product", "Name"), OpenIntentSemanticIds.Field("Product", "Sku"), OpenIntentSemanticIds.Field("Product", "UnitPrice") },
            null, null, Array.Empty<SemanticReadNode>())));

    private SemanticPlan PlanPlaceOrder() => PlanProduct();

    private SemanticPlan PlanSupplier() => Plan(new SemanticOperation(
        new SemanticReadNode(1, OpenIntentSemanticIds.Entity("Supplier"),
            new[] { OpenIntentSemanticIds.Field("Supplier", "Id"), OpenIntentSemanticIds.Field("Supplier", "Name"), OpenIntentSemanticIds.Field("Supplier", "State"), OpenIntentSemanticIds.Field("Supplier", "TotalOrderValue"), OpenIntentSemanticIds.Field("Supplier", "NegotiatedCost") },
            null, null, Array.Empty<SemanticReadNode>())));

    // Authorizes the operation against the trusted contract (see the
    // AllowAllSemanticAuthorizationPolicy comment in Program.cs's DI setup
    // for why an allow-all policy is the right thing here) and plans it
    // through the overload that stamps the resulting SemanticPlan with real
    // authorization provenance, so ExecutionIRCompiler.Compile below
    // (called from PlanFingerprint) doesn't hit "An executable plan must
    // carry authorization provenance before crossing the execution
    // boundary." The previous code called the bare Plan(SemanticOperation)
    // overload, which never attaches a binding at all - unconditionally
    // tripping that guard on every place_order/get_my_orders/get_order call.
    private SemanticPlan Plan(SemanticOperation operation) =>
        planner.Plan(contract, authorizer.AuthorizeWithEvidence(contract, operation));

    private static string PlanFingerprint(SemanticPlan p) => Hash(JsonSerializer.Serialize(new
    {
        semantic = p.Root,
        execution = ExecutionIRCompiler.Compile(p).Root
    }));

    // pg_search (ParadeDB/BM25) isn't installed on a vanilla PostgreSQL
    // image, so it stays opt-in - same gate SupplyChain.Advanced's own
    // PgSearchFactAttribute uses for the PostgresRetrievalCandidateSource
    // test suite (see Semantic/Tests/Retrieval/PostgresRetrievalFactAttribute.cs).
    private static bool PgSearchEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_PGSEARCH"), "1", StringComparison.Ordinal);

    // Approximate retrieval fallback for a supplierName that didn't match
    // any candidate exactly, tried in increasing order of looseness -
    // mirroring PostgresRetrievalCandidateSource's RetrievalStrategy enum:
    //   1. Fuzzy    - pg_trgm trigram similarity (typos, minor misspellings)
    //   2. FullText - native Postgres full-text search (word order, stemming)
    //   3. Search   - pg_search/BM25 (opt-in, FOUNDGINE_POSTGRES_PGSEARCH=1)
    // GraphSimilarity and Vector don't apply here - there's no relationship
    // or embedding to compare against, just a misspelled or loosely-phrased
    // name - and Relational is the exact match this method is a fallback
    // from. Returns the first strategy that finds anything, or null if none
    // of them do.
    private static async Task<(string Strategy, List<Dictionary<string, object?>> Matches)?>
        TryApproximateSupplierMatchAsync(
            NpgsqlConnection c, string state, string supplierName, CancellationToken ct)
    {
        var fuzzy = await TryFuzzyAsync(c, state, supplierName, ct);
        if (fuzzy.Count > 0) return ("fuzzy", fuzzy);

        var fullText = await TryFullTextAsync(c, state, supplierName, ct);
        if (fullText.Count > 0) return ("fulltext", fullText);

        if (PgSearchEnabled)
        {
            var search = await TrySearchAsync(c, state, supplierName, ct);
            if (search.Count > 0) return ("search", search);
        }

        return null;
    }

    // pg_trgm's % operator: true when trigram similarity clears the
    // (session-configurable, default 0.3) similarity threshold. Ships in
    // every stock PostgreSQL image's contrib modules and is provisioned by
    // Database/Program.cs's schema (CREATE EXTENSION IF NOT EXISTS pg_trgm).
    private static async Task<List<Dictionary<string, object?>>> TryFuzzyAsync(
        NpgsqlConnection c, string state, string supplierName, CancellationToken ct)
    {
        const string sql = """
                           SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost,
                                  similarity(supplier_name, @name) AS score
                           FROM suppliers
                           WHERE state = @state AND supplier_name % @name
                           ORDER BY score DESC
                           LIMIT 5
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("state", state.ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", supplierName);
        try
        {
            return await ReadRows(cmd, ct);
        }
        catch (PostgresException)
        {
            // pg_trgm not installed on this instance - degrade to "no
            // fuzzy candidates" instead of failing the whole capability.
            return [];
        }
    }

    // Native Postgres full-text search: catches word-order and stemming
    // differences (e.g. "Industrial Metal" vs "Metal Industries") that
    // trigram similarity can miss.
    private static async Task<List<Dictionary<string, object?>>> TryFullTextAsync(
        NpgsqlConnection c, string state, string supplierName, CancellationToken ct)
    {
        const string sql = """
                           SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost,
                                  ts_rank_cd(to_tsvector('english', supplier_name), websearch_to_tsquery('english', @name)) AS score
                           FROM suppliers
                           WHERE state = @state
                             AND to_tsvector('english', supplier_name) @@ websearch_to_tsquery('english', @name)
                           ORDER BY score DESC
                           LIMIT 5
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("state", state.ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", supplierName);
        try
        {
            return await ReadRows(cmd, ct);
        }
        catch (PostgresException)
        {
            return [];
        }
    }

    // pg_search/BM25 (ParadeDB), the same provider PostgresRetrievalCandidateSource.
    // ExecutePgSearchAsync uses for RetrievalStrategy.Search. Requires the
    // pg_search extension and a BM25 index that this sample does not
    // provision by default, so it's gated by FOUNDGINE_POSTGRES_PGSEARCH=1
    // (checked by the caller) and degrades to "no candidates" rather than
    // throwing if the extension/operator turns out not to be installed.
    private static async Task<List<Dictionary<string, object?>>> TrySearchAsync(
        NpgsqlConnection c, string state, string supplierName, CancellationToken ct)
    {
        const string sql = """
                           SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost,
                                  pdb.score(supplier_id) AS score
                           FROM suppliers
                           WHERE supplier_name ||| @name AND state = @state
                           ORDER BY score DESC
                           LIMIT 5
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("state", state.ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", supplierName);
        try
        {
            return await ReadRows(cmd, ct);
        }
        catch (PostgresException)
        {
            return [];
        }
    }

    private static async Task<List<Dictionary<string, object?>>> ReadRows(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    private static async Task<Dictionary<string, object?>?> ReadSingle(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        return row;
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..24];

    private static int ActorNumber(string actor) => actor switch
    {
        "alice" => 1,
        "bob" => 2,
        "carol" => 3,
        "dave" => 4,
        "admin" => 5,
        _ when actor.StartsWith("customer", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(actor[8..], out var id) => id,
        _ => 0
    };
}