package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.runtime.IFoundgine;
import com.foundgine.runtime.IFoundgineMutations;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.runtime.MutationPlanApproval;
import com.foundgine.runtime.SemanticMutationRequest;
import java.lang.reflect.Proxy;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ExecutionException;
import org.junit.jupiter.api.Test;

/** Parity with the C# MCP adapter: tool names, fail-closed host security, host-context override, mutation approval, JSON-RPC core. */
class McpAdapterParityTest {
    private static final ObjectMapper M = new ObjectMapper();
    private static final String INTENT = "{\"rootEntity\":\"Order\",\"selections\":[{\"field\":\"id\"}]}";
    private static final String MUTATION =
            "{\"operations\":[{\"entity\":1,\"kind\":\"create\",\"fields\":{\"2\":\"x\"},\"returnFields\":[2]}]}";

    static SecurityExecutionContext host(String subject) {
        var now = Instant.now();
        var warrant = SecurityWarrant.ofDefaults("w", "issuer", subject, "aud", List.of(),
                SecurityWarrantConstraints.UNRESTRICTED, now, now.plusSeconds(3600), "n", "k", null, new byte[0]);
        return new SecurityExecutionContext(warrant, subject, "aud");
    }

    static final class Engine {
        final List<ReadIntent> intents = new ArrayList<>();
        final List<SecurityExecutionContext> discovery = new ArrayList<>();
        final IFoundgine proxy = (IFoundgine) Proxy.newProxyInstance(IFoundgine.class.getClassLoader(),
                new Class<?>[] {IFoundgine.class}, (p, m, a) -> {
                    if (m.getName().equals("executeAsync") && a[0] instanceof ReadIntent i) {
                        intents.add(i);
                        return CompletableFuture.completedFuture(new ExecutionResult(List.of(new ExecutionRow(Map.of("id", 7L)))));
                    }
                    if (m.getName().equals("describeCapabilityContract") && a != null && a.length == 1) {
                        discovery.add((SecurityExecutionContext) a[0]);
                        return new com.foundgine.core.semantic.capabilities.SemanticCapabilityContract(1, List.of());
                    }
                    throw new UnsupportedOperationException(m.getName());
                });
    }

    static final class MutEngine {
        final List<SemanticMutationRequest> requests = new ArrayList<>();
        final List<MutationPlanApproval> executed = new ArrayList<>();
        final IFoundgineMutations proxy = (IFoundgineMutations) Proxy.newProxyInstance(
                IFoundgineMutations.class.getClassLoader(), new Class<?>[] {IFoundgineMutations.class}, (p, m, a) -> {
                    switch (m.getName()) {
                        case "dryRun" -> {
                            requests.add((SemanticMutationRequest) a[0]);
                            return new com.foundgine.runtime.MutationDryRunResult("fp-1", List.of(), List.of("create"));
                        }
                        case "approve" -> {
                            var r = (SemanticMutationRequest) a[0];
                            requests.add(r);
                            return new MutationPlanApproval(r, "appr-1", "fp-1", (String) a[1], Instant.now());
                        }
                        case "executeApprovedAsync" -> {
                            executed.add((MutationPlanApproval) a[0]);
                            return CompletableFuture.completedFuture(new MutationExecutionResult(null, "fp-1", "rf", "appr-1", "alice"));
                        }
                        default -> throw new UnsupportedOperationException(m.getName());
                    }
                });
    }

    static Throwable failure(java.util.concurrent.CompletionStage<?> s) throws Exception {
        try {
            s.toCompletableFuture().get();
        } catch (ExecutionException e) {
            Throwable t = e.getCause();
            while (t instanceof CompletionException && t.getCause() != null) t = t.getCause();
            return t;
        }
        return fail("expected failure");
    }

    @Test
    void toolNamesMatchCSharp() {
        assertEquals("foundgine_capabilities", FoundgineMcpToolNames.CAPABILITIES);
        assertEquals("foundgine_query", FoundgineMcpToolNames.QUERY);
        assertEquals("foundgine_query_semantic", FoundgineMcpToolNames.QUERY_SEMANTIC);
        assertEquals("foundgine_mutation_dry_run", FoundgineMcpToolNames.MUTATION_DRY_RUN);
        assertEquals("foundgine_mutation_approve", FoundgineMcpToolNames.MUTATION_APPROVE);
        assertEquals("foundgine_mutation_execute_approved", FoundgineMcpToolNames.MUTATION_EXECUTE_APPROVED);
    }

    @Test
    void missingHostSecurityFailsClosedBeforeExecution() throws Exception {
        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).build();
        assertInstanceOf(SecurityException.class, failure(tools.executeSemanticQueryAsync(INTENT)));
        assertInstanceOf(SecurityException.class, failure(tools.executeQueryAsync(INTENT)));
        assertThrows(SecurityException.class, tools::describeCapabilities);
        assertTrue(e.intents.isEmpty());
    }

    @Test
    void hostSecurityIsBoundToTheIntent() throws Exception {
        var e = new Engine();
        var ctx = host("alice");
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> ctx).build();
        var json = tools.executeSemanticQueryAsync(INTENT).toCompletableFuture().get();
        assertSame(ctx, e.intents.get(0).security());
        JsonNode node = M.readTree(json);
        assertEquals(7, node.get("rows").get(0).get("id").asInt());
        assertTrue(node.has("pageInfo") && node.has("evidence") && node.has("receipt"));
    }

    @Test
    void agentSuppliedSecurityNeverBecomesAuthority() throws Exception {
        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("alice")).build();
        var hostile = "{\"rootEntity\":\"Order\",\"selections\":[{\"field\":\"id\"}],\"security\":{\"subject\":\"root\"},\"tenant\":\"x\"}";
        var t = failure(tools.executeSemanticQueryAsync(hostile));
        assertInstanceOf(IllegalArgumentException.class, t);
        assertTrue(e.intents.isEmpty());
    }

    @Test
    void capabilitiesUseHostContext() {
        var e = new Engine();
        var ctx = host("alice");
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> ctx).build();
        assertTrue(tools.describeCapabilities().contains("capabilities"));
        assertSame(ctx, e.discovery.get(0));
    }

    @Test
    void providerAndFactoryTogetherAreRejected() {
        var e = new Engine();
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpTools.builder(e.proxy)
                .securityContextProvider(() -> host("a")).securityContextFactory(() -> host("a")).build());
        assertThrows(NullPointerException.class, () -> FoundgineMcpTools.builder(null).build());
    }

    @Test
    void lexicalIntentWithoutGrounderReportsNotConfigured() throws Exception {
        var tools = FoundgineMcpTools.builder(new Engine().proxy).securityContextFactory(() -> host("a")).build();
        assertInstanceOf(FoundgineMcpNotConfiguredException.class, failure(tools.executeQueryAsync("show orders")));
        assertInstanceOf(FoundgineMcpNotConfiguredException.class, failure(tools.executeQueryAsync("{\"intent\":\"show orders\"}")));
        assertInstanceOf(IllegalArgumentException.class, failure(tools.executeQueryAsync("   ")));
        assertInstanceOf(IllegalArgumentException.class, failure(tools.executeSemanticQueryAsync("[1]")));
    }

    @Test
    void jsonIntentThroughOpenToolDoesNotNeedGrounder() throws Exception {
        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("a")).build();
        tools.executeQueryAsync(INTENT).toCompletableFuture().get();
        assertEquals(1, e.intents.size());
    }

    @Test
    void mutationWithoutEngineIsNotConfigured() {
        var tools = new FoundgineMcpMutationTools(null, () -> host("a"), null);
        assertThrows(FoundgineMcpNotConfiguredException.class, () -> tools.dryRun(MUTATION));
    }

    @Test
    void mutationFailsClosedWithoutHostSecurity() {
        var m = new MutEngine();
        var tools = new FoundgineMcpMutationTools(m.proxy, null, null);
        assertThrows(SecurityException.class, () -> tools.dryRun(MUTATION));
        assertThrows(SecurityException.class, () -> tools.approve(MUTATION, "root"));
        assertTrue(m.requests.isEmpty());
    }

    @Test
    void mutationApprovalFlowBindsHostContextAndSubject() throws Exception {
        var m = new MutEngine();
        var ctx = host("alice");
        var generator = java.security.KeyPairGenerator.getInstance("RSA");
        generator.initialize(2048);
        var tokenKey = generator.generateKeyPair();
        var signer = FoundgineMcpApprovalToken.rsaSha256Pkcs1Signer("test-key", tokenKey.getPrivate());
        FoundgineMcpApprovalToken.KeyResolver resolver = id -> id.equals("test-key") ? tokenKey.getPublic() : null;
        var tools = new FoundgineMcpMutationTools(m.proxy, () -> ctx, null, signer, resolver,
                FoundgineMcpApprovalTokenPolicy.defaults(new FoundgineMcpApprovalTokenPolicy.ReplayStore() {
                    private final java.util.Set<String> used = java.util.concurrent.ConcurrentHashMap.newKeySet();
                    public boolean tryConsume(String id, long expiresAt) { return used.add(id); }
                }));

        assertTrue(tools.dryRun(MUTATION).contains("fp-1"));
        assertSame(ctx, m.requests.get(0).security());

        var token = tools.approve(MUTATION, "attacker");
        var claims = FoundgineMcpApprovalToken.decodeAndVerify(token, resolver);
        assertEquals("alice", claims.approvedBy());

        var result = tools.executeApprovedAsync(token).toCompletableFuture().get();
        assertTrue(result.contains("appr-1"));
        var executed = m.executed.get(0);
        assertSame(ctx, executed.request().security());
        assertEquals("fp-1", executed.planFingerprint());
        assertEquals(1, executed.request().graph().operations().size());
    }

    @Test
    void executeApprovedRejectsMalformedTokensAndMissingSecurity() throws Exception {
        var m = new MutEngine();
        var generator = java.security.KeyPairGenerator.getInstance("RSA");
        generator.initialize(2048);
        var tokenKey = generator.generateKeyPair();
        var signer = FoundgineMcpApprovalToken.rsaSha256Pkcs1Signer("test-key", tokenKey.getPrivate());
        FoundgineMcpApprovalToken.KeyResolver resolver = id -> id.equals("test-key") ? tokenKey.getPublic() : null;
        var tools = new FoundgineMcpMutationTools(m.proxy, () -> host("a"), null, signer, resolver);
        assertInstanceOf(SecurityException.class, failure(tools.executeApprovedAsync("{}")));
        assertInstanceOf(SecurityException.class, failure(tools.executeApprovedAsync("not json")));
        var noSec = new FoundgineMcpMutationTools(m.proxy, null, null, signer, resolver);
        var token = tools.approve(MUTATION, null);
        assertInstanceOf(SecurityException.class, failure(noSec.executeApprovedAsync(token)));
        assertTrue(m.executed.isEmpty());
    }

    @Test
    void mutationJsonAdapterValidates() {
        assertThrows(IllegalArgumentException.class, () -> MutationJsonAdapter.parse("{\"operations\":[]}"));
        assertThrows(IllegalArgumentException.class, () -> MutationJsonAdapter.parse(
                "{\"operations\":[{\"entity\":-1,\"kind\":\"create\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> MutationJsonAdapter.parse(
                "{\"operations\":[{\"entity\":1,\"kind\":\"delete\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> MutationJsonAdapter.parse(
                "{\"operations\":[{\"entity\":1,\"kind\":\"explode\"}]}"));
        var r = MutationJsonAdapter.parse("{\"operations\":[{\"entity\":18446744073709551615,\"kind\":\"update\","
                + "\"fields\":{\"3\":1.5},\"filter\":{\"and\":[{\"field\":3,\"operator\":\"GTE\",\"value\":2}]}}]}");
        assertNull(r.security());
        assertEquals(-1L, r.graph().operations().get(0).entity().value());
    }

    private FoundgineMcpServer server(Engine e, MutEngine m) {
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("alice")).build();
        var mut = new FoundgineMcpMutationTools(m.proxy, () -> host("alice"), null);
        return FoundgineMcpServer.builder().withReadTools(tools).withMutationTools(mut).build();
    }

    private JsonNode rpc(FoundgineMcpServer s, String body) throws Exception {
        var out = s.handleAsync(body).toCompletableFuture().get();
        return out.isPresent() ? M.readTree(out.get()) : null;
    }

    @Test
    void serverListsAllSixToolsWithReadOnlyHints() throws Exception {
        var s = server(new Engine(), new MutEngine());
        var list = rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}").get("result").get("tools");
        var names = new ArrayList<String>();
        list.forEach(t -> names.add(t.get("name").asText()));
        assertEquals(List.of("foundgine_capabilities", "foundgine_query", "foundgine_query_semantic",
                "foundgine_mutation_dry_run", "foundgine_mutation_approve", "foundgine_mutation_execute_approved"), names);
        assertTrue(list.get(1).get("annotations").get("readOnlyHint").asBoolean());
        assertFalse(list.get(3).get("annotations").get("readOnlyHint").asBoolean());
    }

    @Test
    void serverToolCallsAndErrorMapping() throws Exception {
        var e = new Engine();
        var s = server(e, new MutEngine());
        var ok = rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"foundgine_query_semantic\","
                + "\"arguments\":{\"intentJson\":" + M.writeValueAsString(INTENT) + "}}}").get("result");
        assertFalse(ok.get("isError").asBoolean());
        assertEquals(1, e.intents.size());

        var bad = rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"foundgine_query\","
                + "\"arguments\":{\"intentJson\":\"show orders\"}}}").get("result");
        assertTrue(bad.get("isError").asBoolean());
        assertTrue(bad.get("content").get(0).get("text").asText().contains("not configured"));

        var missing = rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"foundgine_query\",\"arguments\":{}}}")
                .get("result");
        assertTrue(missing.get("isError").asBoolean());
    }

    @Test
    void serverProtocolBasics() throws Exception {
        var s = server(new Engine(), new MutEngine());
        assertNull(rpc(s, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        assertEquals("2025-06-18", rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"1999\"}}")
                .get("result").get("protocolVersion").asText());
        assertEquals(-32601, rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"nope\"}").get("error").get("code").asInt());
        assertEquals(-32700, rpc(s, "{oops").get("error").get("code").asInt());
        assertEquals(-32602, rpc(s, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"zzz\"}}")
                .get("error").get("code").asInt());
    }

    @Test
    void internalFailuresAreNotLeakedToAgents() {
        var msg = FoundgineMcpServer.agentSafeMessage("foundgine_query", new IllegalStateException("jdbc://secret"));
        assertEquals("An error occurred invoking 'foundgine_query'.", msg);
    }

    @Test
    void registryWiringResolvesTools() {
        var e = new Engine();
        var m = new MutEngine();
        var reg = new com.foundgine.runtime.FoundgineServiceRegistry();
        reg.addSingletonInstance(IFoundgine.class, e.proxy);
        reg.addSingletonInstance(IFoundgineMutations.class, m.proxy);
        FoundgineMcpServiceCollectionExtensions.addFoundgineMcp(reg, null, null, () -> host("alice"));
        var tools = reg.getRequiredService(FoundgineMcpTools.class);
        assertSame(tools, reg.getRequiredService(FoundgineMcpTools.class));
        assertNotNull(tools.describeCapabilities());
        assertTrue(reg.getRequiredService(FoundgineMcpMutationTools.class).dryRun(MUTATION).contains("fp-1"));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpServiceCollectionExtensions.addFoundgineMcp(
                new com.foundgine.runtime.FoundgineServiceRegistry(), null, () -> host("a"), () -> host("a")));
    }
}
