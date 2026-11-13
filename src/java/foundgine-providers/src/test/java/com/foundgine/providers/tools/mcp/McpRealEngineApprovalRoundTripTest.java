package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.execution.mutation.IMutationBatchExecutionProvider;
import com.foundgine.core.execution.mutation.IMutationSecurityConformanceEvaluator;
import com.foundgine.core.execution.mutation.MutationSecurityConformanceResult;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.execution.mutation.MutationResult;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContractDiscovery;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.*;
import com.foundgine.runtime.FoundgineMutationEngine;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.NoSuchElementException;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

/**
 * The MCP mutation tools against the real {@link FoundgineMutationEngine} (no reflection proxy): the approval token's
 * re-parsed mutation JSON must reproduce the plan fingerprint at execute time, and a tampered token must be refused
 * before the provider is reached. Uses a real signed warrant, warrant key resolver and capability contract.
 *
 * <p>Compiled and run against the real core/runtime sources with javac 21 and Jackson 2.15.2 (not via Maven).
 */
class McpRealEngineApprovalRoundTripTest {
    private static final ObjectMapper M = new ObjectMapper();
    private static final EntityId CUSTOMER = EntityId.create("Customer");
    private static final FieldId ID = FieldId.create("Customer", "Id");
    private static final FieldId NAME = FieldId.create("Customer", "Name");

    private static String mutation(String name) {
        var entity = Long.toUnsignedString(CUSTOMER.value());
        var nameId = Long.toUnsignedString(NAME.value());
        return "{\"operations\":[{\"entity\":" + entity + ",\"kind\":\"create\",\"fields\":{\"" + nameId + "\":\"" + name
                + "\"},\"returnFields\":[" + nameId + "]}]}";
    }

    /** Stub batch provider that, like a real one, certifies the invariants it owns (the engine names it by class name). */
    static final class StubProvider implements IMutationBatchExecutionProvider, IMutationSecurityConformanceEvaluator {
        private final AtomicInteger batches;
        StubProvider(AtomicInteger batches) { this.batches = batches; }
        @Override public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context) {
            batches.incrementAndGet();
            return new MutationBatchResult(List.of(new MutationResult(1)));
        }
        @Override public MutationSecurityConformanceResult evaluate(ExecutionMutationIR ir) {
            return new MutationSecurityConformanceResult(getClass().getName(), ir.requiredSecurityInvariants(), List.of());
        }
    }

    private static final class Fixture {
        final AtomicInteger batches = new AtomicInteger();
        final FoundgineMutationEngine engine;
        final FoundgineMcpMutationTools tools;
        final KeyPair approvalTokenKey;
        final FoundgineMcpApprovalToken.Signer approvalSigner;

        Fixture() throws Exception {
            var model = new SemanticModelBuilder()
                    .entity(CUSTOMER, "Customer", e -> e.identity(ID, "Id").field(NAME, "Name", String.class))
                    .build();
            var columns = new LinkedHashMap<FieldId, ColumnId>();
            columns.put(ID, new ColumnId(1));
            columns.put(NAME, new ColumnId(2));
            var entitySchema = new MutationEntitySchema(
                    CUSTOMER, "Customer", new LinkedHashSet<>(columns.values()), columns, new ColumnId(1));
            MutationSchema schema = new MutationSchema() {
                @Override public MutationEntitySchema getEntity(EntityId id) {
                    if (CUSTOMER.equals(id)) return entitySchema;
                    throw new NoSuchElementException();
                }
                @Override public MutationRelationshipSchema getRelationship(RelationshipId id) {
                    throw new NoSuchElementException();
                }
            };
            var provider = new StubProvider(batches);
            var policy = new AllowAllSemanticAuthorizationPolicy();
            var gen = KeyPairGenerator.getInstance("RSA");
            gen.initialize(2048);
            KeyPair key = gen.generateKeyPair();
            ISecurityWarrantKeyResolver resolver = keyId -> (RSAPublicKey) key.getPublic();
            engine = new FoundgineMutationEngine(schema, policy, provider, model, resolver, "issuer", new MemorySecurityWarrantReplayStore(), null);

            // Grant exactly what the engine's own contract says create on Customer needs.
            var grants = SemanticCapabilityContractDiscovery.describe(model, policy).capabilities().stream()
                    .filter(c -> c.targetEntityId().equals(CUSTOMER) && c.operation().equalsIgnoreCase("create"))
                    .map(c -> new CapabilityGrant(c.id(), c.operation(), List.of()))
                    .toList();
            assertFalse(grants.isEmpty(), "contract must expose create on Customer");
            var now = Instant.now();
            var warrant = SecurityWarrantSigner.sign(
                    new SecurityWarrant("w-1", "issuer", "alice", "api", grants,
                            new SecurityWarrantConstraints(List.of("tenant-a"), null, null, null, null, null),
                            now.minusSeconds(60), now.plusSeconds(600), "nonce-1", "key-1", null, new byte[0], null,
                            List.of()),
                    key.getPrivate());
            var ctx = new SecurityExecutionContext(warrant, "alice", "api", "tenant-a", "tenant-a");
            var tokenKeyGenerator = KeyPairGenerator.getInstance("RSA");
            tokenKeyGenerator.initialize(2048);
            approvalTokenKey = tokenKeyGenerator.generateKeyPair();
            approvalSigner = FoundgineMcpApprovalToken.rsaSha256Pkcs1Signer("mcp-approval-1", approvalTokenKey.getPrivate());
            tools = new FoundgineMcpMutationTools(engine, () -> ctx, null, approvalSigner,
                    keyId -> keyId.equals("mcp-approval-1") ? approvalTokenKey.getPublic() : null,
                    FoundgineMcpApprovalTokenPolicy.defaults(new FoundgineMcpApprovalTokenPolicy.ReplayStore() {
                        private final java.util.Set<String> used = java.util.concurrent.ConcurrentHashMap.newKeySet();
                        public boolean tryConsume(String id, long expiresAt) { return used.add(id); }
                    }));
        }
    }

    @Test
    void dryRunApproveExecuteRoundTripReproducesTheFingerprint() throws Exception {
        var f = new Fixture();
        var dry = M.readTree(f.tools.dryRun(mutation("Ada")));
        var tokenText = f.tools.approve(mutation("Ada"), "ignored-by-host");
        var claims = FoundgineMcpApprovalToken.decodeAndVerify(tokenText,
                keyId -> keyId.equals("mcp-approval-1") ? f.approvalTokenKey.getPublic() : null);
        assertEquals("alice", claims.approvedBy(), "approver is the host subject, not the agent's claim");
        assertEquals(dry.get("planFingerprint").asText(), claims.planFingerprint());

        var result = M.readTree(f.tools.executeApprovedAsync(tokenText).toCompletableFuture().get());
        assertEquals(1, f.batches.get());
        assertEquals(claims.approvalId(), result.get("approvalId").asText());
        assertEquals(claims.planFingerprint(), result.get("planFingerprint").asText());
    }

    @Test
    void tokenSurvivesAJsonRoundTripThroughTheServer() throws Exception {
        var f = new Fixture();
        var server = FoundgineMcpServer.builder().withMutationTools(f.tools).build();
        var approve = server.handleAsync(M.writeValueAsString(M.createObjectNode().put("jsonrpc", "2.0").put("id", 1)
                        .put("method", "tools/call")
                        .set("params", M.createObjectNode().put("name", "foundgine_mutation_approve")
                                .set("arguments", M.createObjectNode().put("mutationJson", mutation("Ada"))
                                        .put("approvedBy", "x")))))
                .toCompletableFuture().get().orElseThrow();
        var tokenText = M.readTree(approve).at("/result/content/0/text").asText();
        var exec = server.handleAsync(M.writeValueAsString(M.createObjectNode().put("jsonrpc", "2.0").put("id", 2)
                        .put("method", "tools/call")
                        .set("params", M.createObjectNode().put("name", "foundgine_mutation_execute_approved")
                                .set("arguments", M.createObjectNode().put("approvalJson", tokenText)))))
                .toCompletableFuture().get().orElseThrow();
        assertFalse(M.readTree(exec).at("/result/isError").asBoolean(), exec);
        assertEquals(1, f.batches.get());
    }

    @Test
    void tamperedMutationInTheTokenIsRefusedBeforeTheProvider() throws Exception {
        var f = new Fixture();
        var original = f.tools.approve(mutation("Ada"), "x");
        var claims = FoundgineMcpApprovalToken.decodeAndVerify(original,
                keyId -> f.approvalTokenKey.getPublic());
        // Model a token minted with a mismatched plan claim: valid signature, but altered mutation.
        var altered = FoundgineMcpApprovalToken.encode(claims.approvalId(), claims.planFingerprint(),
                claims.approvedBy(), claims.approvedAtUnixMilliseconds(), mutation("Mallory"), f.approvalSigner);
        var failure = assertThrows(java.util.concurrent.ExecutionException.class,
                () -> f.tools.executeApprovedAsync(altered).toCompletableFuture().get());
        assertInstanceOf(IllegalStateException.class, failure.getCause());
        assertTrue(failure.getCause().getMessage().contains("changed after approval"), failure.getCause().getMessage());
        assertEquals(0, f.batches.get());
    }

    @Test
    void tokenIsBoundToTheCurrentCallersEntitlementAtExecuteTime() throws Exception {
        var f = new Fixture();
        var token = f.tools.approve(mutation("Ada"), "x");
        var noSecurity = new FoundgineMcpMutationTools(f.engine, null, null, f.approvalSigner,
                keyId -> f.approvalTokenKey.getPublic());
        var failure = assertThrows(java.util.concurrent.ExecutionException.class,
                () -> noSecurity.executeApprovedAsync(token).toCompletableFuture().get());
        assertInstanceOf(SecurityException.class, failure.getCause());
        assertEquals(0, f.batches.get());
    }

    @Test
    void alreadyCancelledTokenStopsExecutionBeforeTheProvider() throws Exception {
        var f = new Fixture();
        var token = f.tools.approve(mutation("Ada"), "x");
        try (var source = new com.foundgine.core.execution.CancellationTokenSource()) {
            source.cancel();
            // a failed-with-CancellationException future throws CancellationException (not ExecutionException) from get()
            assertThrows(Exception.class,
                    () -> f.tools.executeApprovedAsync(token, source.token()).toCompletableFuture().get());
        }
        assertEquals(0, f.batches.get());
    }
}
