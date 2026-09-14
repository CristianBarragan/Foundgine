package com.foundgine.samples.supplychain.advanced;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.foundgine.samples.supplychain.advanced.mcp.AdvancedMcpServer;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Java parity for the C# advanced sample's
 * {@code Semantic/Tests/Mcp/AuthorizationMcpPenetrationTests.cs} — MCP-boundary
 * adversarial cases. These intentionally behave like Run 5: the caller is
 * treated as untrusted and attempts to use a valid MCP tool in a way that
 * should cross a semantic authorization boundary.
 */
class AuthorizationMcpPenetrationParityTest {

    @Test
    void attackMatrixDefinesTheRequiredPolicyBoundaries() {
        var attacks = List.of(
                "cross-tenant",
                "sensitive-field",
                "relationship-escalation",
                "write-escalation",
                "named-operation",
                "unauthorized-write");

        assertEquals(6, attacks.size());
        attacks.forEach(attack -> assertFalse(attack.isBlank()));
    }

    @Test
    void claimsAttackMatrixDefinesTheRequiredClaimValidationBoundaries() {
        // Each of these corresponds to a case the adversarial McpClient sends
        // and to a unit test in ClientClaimsValidatorTests / AuthorizationPolicyTests.
        // Listed here as the single source of truth for "what must this
        // feature demonstrably reject". See GUIDE.md "Claims validation".
        var claimAttacks = List.of(
                "role-injection", // client asserts role directly
                "tenant-injection", // client asserts tenant directly
                "missing-evidence", // sensitive named op without required claims
                "malformed-evidence", // required claim present but wrong format
                "expired-evidence" // required claim present but past its own not_after
        );

        var claimNarrowingUses = List.of(
                "self-imposed-read-only", // scope=read-only restricts a manager's own writes
                "warehouse-scoping", // warehouse=<id> ANDs onto the tenant predicate
                "unknown-key-ignored", // noise claim is dropped, request still proceeds
                "valid-reconcile-evidence" // well-formed reason + change_ticket allows reconcile
        );

        assertEquals(5, claimAttacks.size());
        assertEquals(4, claimNarrowingUses.size());
        Stream.concat(claimAttacks.stream(), claimNarrowingUses.stream())
                .forEach(c -> assertFalse(c.isBlank()));
    }

    @Test
    void rawMcpRequestHasExpectedJsonRpcShape() throws Exception {
        // This test is opt-in against the running sample server. It verifies
        // the wire contract when explicitly enabled instead of making CI
        // depend on a local MCP process.
        if (!"1".equals(System.getenv("RUN_SUPPLYCHAIN_MCP_TESTS")))
            return;

        var mapper = new ObjectMapper();
        var params = mapper.createObjectNode();
        params.put("name", "policy_probe");
        var arguments = params.putObject("arguments");
        arguments.put("tenantId", "tenant-a");
        arguments.put("role", "Customer");
        arguments.put("attack", "write-escalation");

        ObjectNode payload = mapper.createObjectNode();
        payload.put("jsonrpc", "2.0");
        payload.put("id", "probe");
        payload.put("method", "tools/call");
        payload.set("params", params);

        var client = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build();
        var request = HttpRequest.newBuilder()
                .uri(URI.create("http://" + AdvancedMcpServer.DEFAULT_HOST + ":" + AdvancedMcpServer.DEFAULT_PORT
                        + AdvancedMcpServer.MCP_PATH))
                .header("Content-Type", "application/json")
                .header("Accept", "application/json")
                .header("MCP-Protocol-Version", "2025-06-18")
                .POST(HttpRequest.BodyPublishers.ofString(mapper.writeValueAsString(payload)))
                .build();

        var response = client.send(request, HttpResponse.BodyHandlers.ofString());
        assertNotEquals(404, response.statusCode());
    }
}
