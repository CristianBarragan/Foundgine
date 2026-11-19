using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Intent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// The canonical intent boundary: one provider-neutral intent, run through a real Foundgine engine
/// (resolution, authorization, planning, execution) with the caller supplied by the host. Each test shows
/// one thing the semantic layer does that a hand-written endpoint would have to re-implement. Mirrors the
/// Java <c>CanonicalIntentEndpointTest</c>.
/// </summary>
public sealed class CanonicalIntentEndpointTests
{
    private static readonly SupplyChainIntentService Service = new(SupplyChainData.Seed());

    private const string SuppliersWithRisk =
        """{"rootEntity":"Supplier","selections":[{"field":"Name"},{"field":"RiskScore"}]}""";

    private const string SupplierNames =
        """{"rootEntity":"Supplier","selections":[{"field":"Name"}]}""";

    private static IntentCaller Caller(string? tenant, string? role) =>
        IntentCaller.Resolve(tenant, role, Service.KnownTenants);

    private static async Task<List<Dictionary<string, object?>>> Rows(IntentCaller caller, string intent) =>
        (List<Dictionary<string, object?>>)(await Service.ExecuteAsync(caller, intent))["rows"]!;

    private static async Task<SupplyChainIntentService.Failure> FailureOf(IntentCaller caller, string intent)
    {
        try
        {
            await Service.ExecuteAsync(caller, intent);
        }
        catch (Exception e)
        {
            return SupplyChainIntentService.Classify(e);
        }

        throw new XunitException("Expected the intent to fail.");
    }

    [Fact]
    public async Task Same_intent_returns_different_fields_per_role()
    {
        var analyst = await Rows(Caller("tenant-a", "analyst"), SuppliersWithRisk);
        var operatorRows = await Rows(Caller("tenant-a", "warehouse_operator"), SuppliersWithRisk);

        Assert.Equal(2, analyst.Count);
        Assert.All(analyst, r => Assert.True(r.ContainsKey("Supplier.RiskScore")));
        // The operator asked for RiskScore too; the policy removed it instead of failing the intent.
        Assert.Equal(2, operatorRows.Count);
        Assert.All(operatorRows, r => Assert.False(r.ContainsKey("Supplier.RiskScore")));
        Assert.All(operatorRows, r => Assert.True(r.ContainsKey("Supplier.Name")));
    }

    [Fact]
    public void Explain_shows_what_the_callers_policy_left_of_the_intent()
    {
        var explained = Service.Explain(Caller("tenant-a", "warehouse_operator"), SuppliersWithRisk);
        var authorization = (Dictionary<string, object?>)explained["authorization"]!;
        Assert.Equal(new List<string> { "Supplier.RiskScore" }, (List<string>)authorization["prunedFields"]!);

        var asAnalyst = Service.Explain(Caller("tenant-a", "analyst"), SuppliersWithRisk);
        Assert.Empty((List<string>)((Dictionary<string, object?>)asAnalyst["authorization"]!)["prunedFields"]!);
        Assert.NotNull(explained["contract"]);
    }

    [Fact]
    public async Task Tenant_isolation_comes_from_the_policy_not_the_intent()
    {
        var tenantA = await Rows(Caller("tenant-a", "analyst"), SupplierNames);
        var tenantB = await Rows(Caller("tenant-b", "analyst"), SupplierNames);

        Assert.Equal(
            new[] { "Kiwi Components", "Pacific Semiconductors" },
            tenantA.Select(r => (string)r["Supplier.Name"]!).Order().ToArray());
        Assert.Equal(new[] { "Global Metals" }, tenantB.Select(r => (string)r["Supplier.Name"]!).ToArray());
    }

    [Fact]
    public async Task Denied_entity_is_forbidden_and_named_by_semantic_name()
    {
        var failure = await FailureOf(Caller("tenant-a", "customer"), SupplierNames);

        Assert.Equal(403, failure.Status);
        Assert.Equal("forbidden", failure.Type);
        Assert.Contains("'Supplier'", failure.Message);
        Assert.DoesNotContain("EntityId", failure.Message);
    }

    [Fact]
    public async Task Aliases_resolve_to_the_semantic_entity()
    {
        var rows = await Rows(Caller("tenant-a", "analyst"),
            """{"rootEntity":"Vendor","selections":[{"field":"Name"}]}""");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.ContainsKey("Supplier.Name")));
    }

    [Fact]
    public async Task Relationship_name_expands_to_the_declared_path_through_the_model()
    {
        const string intent =
            """{"rootEntity":"Product","selections":[{"field":"Name"},{"relationship":"shipments","children":[{"field":"Status"}]}]}""";
        var caller = Caller("tenant-a", "analyst");

        var rows = await Rows(caller, intent);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.ContainsKey("Product.Name") && r.ContainsKey("Shipment.Status")));

        // The intent says "shipments"; Foundgine resolved the four-entity path.
        var resolution = (List<Dictionary<string, object?>>)Service.Explain(caller, intent)["resolution"]!;
        Assert.Equal(
            new[] { "Product", "PurchaseOrderLine", "PurchaseOrder", "Shipment" },
            resolution.Select(n => (string)n["entity"]!).ToArray());
    }

    [Fact]
    public async Task Filter_order_and_limit_are_intent_not_endpoint_parameters()
    {
        var rows = await Rows(Caller("tenant-a", "analyst"),
            """
            {"rootEntity":"Product","selections":[{"field":"Sku"},{"field":"SafetyStock"}],
             "filter":{"kind":"field","field":"Category","operator":"Eq","value":"Electronics"},
             "order":[{"field":"SafetyStock","direction":"Desc"}],"limit":2}
            """);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "CAP-X", "RES-X" }, rows.Select(r => (string)r["Product.Sku"]!).ToArray());
    }

    [Fact]
    public async Task Every_execution_carries_evidence_and_a_receipt()
    {
        var result = await Service.ExecuteAsync(Caller("tenant-a", "analyst"), SupplierNames);

        var receipt = (Dictionary<string, object?>)result["receipt"]!;
        var evidence = (Dictionary<string, object?>)result["evidence"]!;
        Assert.Equal("succeeded", receipt["status"]);
        Assert.Equal("in-memory", receipt["provider"]);
        // The tenant predicate was applied to the root node.
        Assert.Equal(new[] { 0 }, ((IEnumerable<int>)evidence["authorizedNodeIds"]!).ToArray());
    }

    [Fact]
    public async Task Identity_and_unknown_names_in_the_intent_are_rejected()
    {
        var analyst = Caller("tenant-a", "analyst");

        // Identity-shaped properties are not part of the intent language.
        var smuggled = await FailureOf(analyst,
            """{"rootEntity":"Supplier","selections":[{"field":"Name"}],"tenant":"tenant-b"}""");
        Assert.Equal(400, smuggled.Status);
        Assert.Equal("invalid_intent", smuggled.Type);

        var unknownEntity = await FailureOf(analyst,
            """{"rootEntity":"Nope","selections":[{"field":"Name"}]}""");
        Assert.Equal(400, unknownEntity.Status);

        var unknownField = await FailureOf(analyst,
            """{"rootEntity":"Supplier","selections":[{"field":"Nmae"}]}""");
        Assert.Equal(400, unknownField.Status);

        var notJson = await FailureOf(analyst, "not json");
        Assert.Equal(400, notJson.Status);
    }

    [Fact]
    public void Unknown_tenant_or_role_is_forbidden_and_defaults_are_least_privileged()
    {
        Assert.Throws<SecurityException>(() => { _ = Caller("tenant-z", "analyst"); });
        Assert.Throws<SecurityException>(() => { _ = Caller("tenant-a", "root"); });
        Assert.Throws<SecurityException>(() => { _ = Caller("../x", "analyst"); });
        // Enum.TryParse would accept these; the caller resolver must not.
        Assert.Throws<SecurityException>(() => { _ = Caller("tenant-a", "1"); });
        Assert.Throws<SecurityException>(() => { _ = Caller("tenant-a", "Analyst,SupplyChainManager"); });
        Assert.Throws<SecurityException>(() => { _ = Caller("tenant-a\nx", "analyst"); });

        var defaults = Caller(null, null);
        Assert.Equal("tenant-a", defaults.TenantId);
        Assert.Equal(IntentCaller.DefaultRole, defaults.Role);
        Assert.Equal(SupplyChainRole.WarehouseOperator, Caller("tenant-a", "warehouse-operator").Role);
        Assert.Equal("WAREHOUSE_OPERATOR", Caller("tenant-a", "WarehouseOperator").RoleName);
    }

    [Fact]
    public void Capabilities_describe_what_the_caller_may_read()
    {
        var customer = Service.Capabilities(Caller("tenant-a", "customer"));
        var analyst = Service.Capabilities(Caller("tenant-a", "analyst"));

        Assert.NotNull(customer["intentShape"]);
        Assert.Equal("DENIED", ReadAccess(customer, "Supplier"));
        Assert.Equal("ALLOWED", ReadAccess(customer, "Product"));
        // Supplier is readable only inside the caller's tenant.
        Assert.Equal("CONDITIONAL", ReadAccess(analyst, "Supplier"));
    }

    private static string ReadAccess(Dictionary<string, object?> capabilities, string entity)
    {
        foreach (var item in (List<Dictionary<string, object?>>)capabilities["entities"]!)
            if ((string)item["entity"]! == entity)
                return (string)((Dictionary<string, object?>)item["read"]!)["access"]!;
        throw new XunitException($"No capability entry for {entity}");
    }

    [Fact]
    public async Task Mcp_tool_uses_the_same_boundary_and_reports_classified_failures()
    {
        using var intent = JsonDocument.Parse(
            """{"rootEntity":"Vendor","selections":[{"field":"Name"}]}""");

        var text = await IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "analyst"), intent.RootElement, null);
        using var payload = JsonDocument.Parse(text);
        Assert.Equal(2, payload.RootElement.GetProperty("rowCount").GetInt32());

        var explain = await IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "analyst"), intent.RootElement, "explain");
        Assert.Contains("prunedFields", explain);
        var capabilities = await IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "analyst"), null, "capabilities");
        Assert.Contains("intentShape", capabilities);

        // An already-serialized intent string is accepted too.
        using var asString = JsonDocument.Parse(JsonSerializer.Serialize(SupplierNames));
        Assert.Contains("rowCount", await IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "analyst"), asString.RootElement, "execute"));

        using var denied = JsonDocument.Parse(SupplierNames);
        var error = await Record.ExceptionAsync(() => IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "customer"), denied.RootElement, null));
        Assert.NotNull(error);
        Assert.StartsWith("forbidden:", IntentEndpoint.ToolFailureMessage(error!));

        var missing = await Record.ExceptionAsync(() => IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "analyst"), null, "execute"));
        Assert.StartsWith("invalid_intent:", IntentEndpoint.ToolFailureMessage(missing!));
        var badMode = await Record.ExceptionAsync(() => IntentEndpoint.CallToolAsync(
            Service, Caller("tenant-a", "analyst"), intent.RootElement, "drop"));
        Assert.StartsWith("invalid_intent:", IntentEndpoint.ToolFailureMessage(badMode!));
    }

    [Fact]
    public async Task Http_endpoint_takes_the_caller_from_headers_only()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapIntentEndpoints(Service);
        await app.StartAsync();
        try
        {
            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

            // Analyst sees RiskScore, and the response echoes the effective caller.
            var ok = await Post(client, "/intent", "analyst", SuppliersWithRisk);
            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal("ANALYST", ok.Json.GetProperty("caller").GetProperty("role").GetString());
            Assert.True(ok.Json.GetProperty("rows")[0].TryGetProperty("Supplier.RiskScore", out _));

            // No role header means the least privileged role, which may not read suppliers.
            var denied = await Post(client, "/intent", null, SupplierNames);
            Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
            Assert.Equal("forbidden", denied.Json.GetProperty("error").GetProperty("type").GetString());

            // Identity in the body is rejected; it never reaches the engine.
            var smuggled = await Post(client, "/intent", "analyst",
                """{"rootEntity":"Supplier","selections":[{"field":"Name"}],"role":"analyst"}""");
            Assert.Equal(HttpStatusCode.BadRequest, smuggled.Status);

            // Unknown tenant header is refused, not treated as an empty tenant.
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/intent")
                   {
                       Content = new StringContent(SupplierNames, Encoding.UTF8, "application/json")
                   })
            {
                request.Headers.Add(IntentCaller.RoleHeader, "analyst");
                request.Headers.Add(IntentCaller.TenantHeader, "tenant-z");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            // Wrong method and oversized bodies are refused before any parsing.
            using var get = await client.GetAsync("/intent");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
            Assert.Contains("POST", string.Join(",", get.Content.Headers.Allow));
            var oversized = await Post(client, "/intent", "analyst", new string('a', 70_000));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.Status);
            var unknown = await Post(client, "/intent/nope", "analyst", SupplierNames);
            Assert.Equal(HttpStatusCode.NotFound, unknown.Status);

            // Explain and capabilities.
            var explain = await Post(client, "/intent/explain", "warehouse_operator", SuppliersWithRisk);
            Assert.Equal(HttpStatusCode.OK, explain.Status);
            Assert.Equal("Supplier.RiskScore",
                explain.Json.GetProperty("authorization").GetProperty("prunedFields")[0].GetString());

            using var request2 = new HttpRequestMessage(HttpMethod.Get, "/intent/capabilities");
            request2.Headers.Add(IntentCaller.RoleHeader, "customer");
            using var capabilities = await client.SendAsync(request2);
            Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Mcp_transport_takes_the_caller_from_headers_and_never_from_arguments()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Service);
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<IntentMcpTools>();
        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        try
        {
            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

            // Analyst through the Vendor alias: same service, same header-based caller.
            var ok = await CallTool(client, "analyst",
                """{"intent":{"rootEntity":"Vendor","selections":[{"field":"Name"}]}}""");
            Assert.False(IsError(ok));
            using var payload = JsonDocument.Parse(ok.GetProperty("content")[0].GetProperty("text").GetString()!);
            Assert.Equal(2, payload.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal("ANALYST", payload.RootElement.GetProperty("caller").GetProperty("role").GetString());

            // No role header: least privileged role, so the tool reports a classified failure.
            var denied = await CallTool(client, null,
                """{"intent":{"rootEntity":"Supplier","selections":[{"field":"Name"}]}}""");
            Assert.True(IsError(denied));
            Assert.StartsWith("forbidden:", denied.GetProperty("content")[0].GetProperty("text").GetString()!);

            // A role smuggled in the arguments is not part of the tool contract and must not elevate anything.
            var smuggled = await CallTool(client, null,
                """{"intent":{"rootEntity":"Supplier","selections":[{"field":"Name"}]},"role":"analyst"}""");
            Assert.True(IsError(smuggled));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>The MCP SDK omits <c>isError</c> from a successful result, so absence means false.</summary>
    private static bool IsError(JsonElement result) =>
        result.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;

    /// <summary>Calls <c>foundgine_intent</c> over streamable HTTP and returns the JSON-RPC <c>result</c>.</summary>
    private static async Task<JsonElement> CallTool(HttpClient client, string? role, string argumentsJson)
    {
        var body =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"foundgine_intent\",\"arguments\":"
            + argumentsJson + "}}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (role is not null) request.Headers.Add(IntentCaller.RoleHeader, role);

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        // Streamable HTTP may answer as a single JSON document or as one server-sent "data:" event.
        var json = text.TrimStart().StartsWith('{')
            ? text
            : string.Join("", text.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l[5..].Trim()));
        return JsonDocument.Parse(json).RootElement.GetProperty("result").Clone();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Post(
        HttpClient client, string url, string? role, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (role is not null) request.Headers.Add(IntentCaller.RoleHeader, role);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }
}