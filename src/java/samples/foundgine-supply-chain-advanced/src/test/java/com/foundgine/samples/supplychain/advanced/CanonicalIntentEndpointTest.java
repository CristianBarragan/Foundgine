package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.intent.IntentCaller;
import com.foundgine.samples.supplychain.advanced.intent.SupplyChainIntentService;
import com.foundgine.samples.supplychain.advanced.mcp.AdvancedMcpServer;
import com.foundgine.samples.supplychain.advanced.mutation.AdvancedMcpFacade;

import org.junit.jupiter.api.Test;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * The canonical intent boundary: one provider-neutral intent, run through a real Foundgine engine
 * (resolution, authorization, planning, execution) with the caller supplied by the host. Each test
 * shows one thing the semantic layer does that a hand-written endpoint would have to re-implement.
 */
class CanonicalIntentEndpointTest {
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final SupplyChainIntentService SERVICE =
            new SupplyChainIntentService(SupplyChainData.seed());

    private static final String SUPPLIERS_WITH_RISK =
            "{\"rootEntity\":\"Supplier\",\"selections\":[{\"field\":\"Name\"},{\"field\":\"RiskScore\"}]}";
    private static final String SUPPLIER_NAMES =
            "{\"rootEntity\":\"Supplier\",\"selections\":[{\"field\":\"Name\"}]}";

    private static IntentCaller caller(String tenant, String role) {
        return IntentCaller.resolve(tenant, role, SERVICE.knownTenants());
    }

    @SuppressWarnings("unchecked")
    private static List<Map<String, Object>> rows(IntentCaller caller, String intent) {
        var result = SERVICE.execute(caller, intent).toCompletableFuture().join();
        return (List<Map<String, Object>>) result.get("rows");
    }

    private static SupplyChainIntentService.Failure failureOf(IntentCaller caller, String intent) {
        try {
            SERVICE.execute(caller, intent).toCompletableFuture().join();
        } catch (Throwable t) {
            return SupplyChainIntentService.failure(t);
        }
        return fail("Expected the intent to fail.");
    }

    @Test
    void sameIntentReturnsDifferentFieldsPerRole() {
        var analyst = rows(caller("tenant-a", "analyst"), SUPPLIERS_WITH_RISK);
        var operator = rows(caller("tenant-a", "warehouse_operator"), SUPPLIERS_WITH_RISK);

        assertEquals(2, analyst.size());
        assertTrue(analyst.stream().allMatch(r -> r.containsKey("Supplier.RiskScore")));
        // The operator asked for RiskScore too; the policy removed it instead of failing the intent.
        assertEquals(2, operator.size());
        assertTrue(operator.stream().noneMatch(r -> r.containsKey("Supplier.RiskScore")));
        assertTrue(operator.stream().allMatch(r -> r.containsKey("Supplier.Name")));
    }

    @Test
    @SuppressWarnings("unchecked")
    void explainShowsWhatTheCallersPolicyLeftOfTheIntent() {
        var explained = SERVICE.explain(caller("tenant-a", "warehouse_operator"), SUPPLIERS_WITH_RISK);

        var authorization = (Map<String, Object>) explained.get("authorization");
        assertEquals(List.of("Supplier.RiskScore"), authorization.get("prunedFields"));

        var asAnalyst = SERVICE.explain(caller("tenant-a", "analyst"), SUPPLIERS_WITH_RISK);
        assertEquals(
                List.of(),
                ((Map<String, Object>) asAnalyst.get("authorization")).get("prunedFields"));
        assertNotNull(explained.get("contract"));
    }

    @Test
    void tenantIsolationComesFromThePolicyNotTheIntent() {
        var tenantA = rows(caller("tenant-a", "analyst"), SUPPLIER_NAMES);
        var tenantB = rows(caller("tenant-b", "analyst"), SUPPLIER_NAMES);

        assertEquals(
                Set.of("Kiwi Components", "Pacific Semiconductors"),
                Set.copyOf(tenantA.stream().map(r -> r.get("Supplier.Name")).toList()));
        assertEquals(List.of("Global Metals"), tenantB.stream().map(r -> r.get("Supplier.Name")).toList());
    }

    @Test
    void deniedEntityIsForbiddenAndNamedBySemanticName() {
        var failure = failureOf(caller("tenant-a", "customer"), SUPPLIER_NAMES);

        assertEquals(403, failure.status());
        assertEquals("forbidden", failure.type());
        assertTrue(failure.message().contains("'Supplier'"), failure.message());
        assertFalse(failure.message().contains("EntityId["), failure.message());
    }

    @Test
    void aliasesResolveToTheSemanticEntity() {
        var rows = rows(
                caller("tenant-a", "analyst"),
                "{\"rootEntity\":\"Vendor\",\"selections\":[{\"field\":\"Name\"}]}");

        assertEquals(2, rows.size());
        assertTrue(rows.stream().allMatch(r -> r.containsKey("Supplier.Name")));
    }

    @Test
    @SuppressWarnings("unchecked")
    void relationshipNameExpandsToTheDeclaredPathThroughTheModel() {
        var intent =
                "{\"rootEntity\":\"Product\",\"selections\":[{\"field\":\"Name\"},"
                        + "{\"relationship\":\"shipments\",\"children\":[{\"field\":\"Status\"}]}]}";
        var caller = caller("tenant-a", "analyst");

        var rows = rows(caller, intent);
        assertFalse(rows.isEmpty());
        assertTrue(rows.stream().allMatch(r -> r.containsKey("Product.Name") && r.containsKey("Shipment.Status")));

        // The intent says "shipments"; Foundgine resolved the four-entity path.
        var resolution = (List<Map<String, Object>>) SERVICE.explain(caller, intent).get("resolution");
        assertEquals(
                List.of("Product", "PurchaseOrderLine", "PurchaseOrder", "Shipment"),
                resolution.stream().map(n -> n.get("entity")).toList());
    }

    @Test
    void filterOrderAndLimitAreIntentNotEndpointParameters() {
        var rows = rows(
                caller("tenant-a", "analyst"),
                "{\"rootEntity\":\"Product\",\"selections\":[{\"field\":\"Sku\"},{\"field\":\"UnitPrice\"}],"
                        + "\"filter\":{\"kind\":\"field\",\"field\":\"Category\",\"operator\":\"Eq\",\"value\":\"Electronics\"},"
                        + "\"order\":[{\"field\":\"UnitPrice\",\"direction\":\"Desc\"}],\"limit\":2}");

        assertEquals(2, rows.size());
        assertEquals(List.of("RES-X", "CAP-X"), rows.stream().map(r -> r.get("Product.Sku")).toList());
    }

    @Test
    @SuppressWarnings("unchecked")
    void everyExecutionCarriesEvidenceAndAReceipt() {
        var result =
                SERVICE.execute(caller("tenant-a", "analyst"), SUPPLIER_NAMES).toCompletableFuture().join();

        var receipt = (Map<String, Object>) result.get("receipt");
        var evidence = (Map<String, Object>) result.get("evidence");
        assertEquals("succeeded", receipt.get("status"));
        assertEquals("in-memory", receipt.get("provider"));
        // The tenant predicate was applied to the root node.
        assertEquals(List.of(0), evidence.get("authorizedNodeIds"));
    }

    @Test
    void identityAndUnknownNamesInTheIntentAreRejected() {
        var analyst = caller("tenant-a", "analyst");

        // Identity-shaped properties are not part of the intent language.
        var smuggled = failureOf(analyst,
                "{\"rootEntity\":\"Supplier\",\"selections\":[{\"field\":\"Name\"}],\"tenant\":\"tenant-b\"}");
        assertEquals(400, smuggled.status());
        assertEquals("invalid_intent", smuggled.type());

        var unknownEntity = failureOf(analyst, "{\"rootEntity\":\"Nope\",\"selections\":[{\"field\":\"Name\"}]}");
        assertEquals(400, unknownEntity.status());

        var unknownField = failureOf(analyst, "{\"rootEntity\":\"Supplier\",\"selections\":[{\"field\":\"Nmae\"}]}");
        assertEquals(400, unknownField.status());
    }

    @Test
    void unknownTenantOrRoleIsForbiddenAndDefaultsAreLeastPrivileged() {
        assertThrows(SecurityException.class, () -> caller("tenant-z", "analyst"));
        assertThrows(SecurityException.class, () -> caller("tenant-a", "root"));
        assertThrows(SecurityException.class, () -> caller("../x", "analyst"));

        var defaults = caller(null, null);
        assertEquals("tenant-a", defaults.tenantId());
        assertEquals(IntentCaller.DEFAULT_ROLE, defaults.role());
    }

    @Test
    @SuppressWarnings("unchecked")
    void capabilitiesDescribeWhatTheCallerMayRead() {
        var customer = SERVICE.capabilities(caller("tenant-a", "customer"));
        var analyst = SERVICE.capabilities(caller("tenant-a", "analyst"));

        assertNotNull(customer.get("intentShape"));
        assertEquals("DENIED", readAccess(customer, "Supplier"));
        assertEquals("ALLOWED", readAccess(customer, "Product"));
        // Supplier is readable only inside the caller's tenant.
        assertEquals("CONDITIONAL", readAccess(analyst, "Supplier"));
    }

    @SuppressWarnings("unchecked")
    private static String readAccess(Map<String, Object> capabilities, String entity) {
        for (var item : (List<Map<String, Object>>) capabilities.get("entities")) {
            if (entity.equals(item.get("entity"))) {
                return (String) ((Map<String, Object>) item.get("read")).get("access");
            }
        }
        return fail("No capability entry for " + entity);
    }

    @Test
    void httpAndMcpUseTheSameBoundaryAndTheCallerComesFromHeadersOnly() throws Exception {
        var auth = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.SUPPLY_CHAIN_MANAGER, false);
        var server = new AdvancedMcpServer(new AdvancedMcpFacade(SupplyChainData.seed(), auth), "localhost", 0);
        server.start();
        try {
            var base = "http://localhost:" + server.port();
            var client = HttpClient.newHttpClient();

            // HTTP: analyst sees RiskScore, and the response echoes the effective caller.
            var ok = post(client, base + "/intent", "analyst", SUPPLIERS_WITH_RISK);
            assertEquals(200, ok.statusCode());
            var body = JSON.readTree(ok.body());
            assertEquals("ANALYST", body.at("/caller/role").asText());
            assertTrue(body.at("/rows/0").has("Supplier.RiskScore"));

            // HTTP: no role header means the least privileged role, which may not read suppliers.
            var denied = post(client, base + "/intent", null, SUPPLIER_NAMES);
            assertEquals(403, denied.statusCode());
            assertEquals("forbidden", JSON.readTree(denied.body()).at("/error/type").asText());

            // HTTP: identity in the body is rejected; it never reaches the engine.
            var smuggled = post(client, base + "/intent", "analyst",
                    "{\"rootEntity\":\"Supplier\",\"selections\":[{\"field\":\"Name\"}],\"role\":\"analyst\"}");
            assertEquals(400, smuggled.statusCode());

            // HTTP: wrong method and oversized bodies are refused before any parsing.
            var get = client.send(HttpRequest.newBuilder(URI.create(base + "/intent")).GET().build(),
                    HttpResponse.BodyHandlers.ofString());
            assertEquals(405, get.statusCode());
            var oversized = post(client, base + "/intent", "analyst", "a".repeat(70_000));
            assertEquals(413, oversized.statusCode());

            // MCP: the same service, through the foundgine_intent tool, same header-based caller.
            var mcp = post(client, base + "/mcp", "analyst",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"foundgine_intent\","
                            + "\"arguments\":{\"intent\":{\"rootEntity\":\"Vendor\",\"selections\":[{\"field\":\"Name\"}]}}}}");
            var tool = JSON.readTree(mcp.body());
            assertFalse(tool.at("/result/isError").asBoolean());
            JsonNode payload = JSON.readTree(tool.at("/result/content/0/text").asText());
            assertEquals(2, payload.get("rowCount").asInt());

            var mcpDenied = post(client, base + "/mcp", null,
                    "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"foundgine_intent\","
                            + "\"arguments\":{\"intent\":{\"rootEntity\":\"Supplier\",\"selections\":[{\"field\":\"Name\"}]}}}}");
            var toolDenied = JSON.readTree(mcpDenied.body());
            assertTrue(toolDenied.at("/result/isError").asBoolean());
            assertTrue(toolDenied.at("/result/content/0/text").asText().startsWith("forbidden:"));
        } finally {
            server.stop();
        }
    }

    private static HttpResponse<String> post(HttpClient client, String url, String role, String body)
            throws Exception {
        var request = HttpRequest.newBuilder(URI.create(url))
                .POST(HttpRequest.BodyPublishers.ofString(body));
        if (role != null) request.header(IntentCaller.ROLE_HEADER, role);
        return client.send(request.build(), HttpResponse.BodyHandlers.ofString());
    }
}
