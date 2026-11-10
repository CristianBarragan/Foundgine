package com.foundgine.runtime;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.ISecurityInvariantProviderCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.*;

import org.junit.jupiter.api.Test;

import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.List;
import java.util.concurrent.CompletableFuture;

/**
 * Mirrors C# TenantFirstGateTests. Step 78: the warrant/tenant gate must run before the request
 * is resolved against the semantic model, so denials never carry schema detail.
 */
class TenantFirstGateParityTest {
    private static final String GATE_MESSAGE =
            "The warrant does not authorize the requested subject or tenant scope.";
    private static final String PROBE = "ZzProbeEntity";
    /** Text of the resolver's unknown-entity error; present only when resolution ran. */
    private static final String RESOLVER_MARKER = "no semantic descriptor";

    @Test
    void wrongTenantProbeOfUnknownEntityGetsGenericDenial() throws Exception {
        var f = new Fixture();
        var ex =
                assertThrows(
                        SecurityException.class,
                        () -> f.engine.dryRun(f.unknownEntityRequest("alice", "tenant-b")));
        assertEquals(GATE_MESSAGE, ex.getMessage());
        assertFalse(ex.getMessage().contains(PROBE));
        assertFalse(ex.getMessage().contains(RESOLVER_MARKER));
    }

    @Test
    void missingTenantProbeOfUnknownEntityGetsGenericDenial() throws Exception {
        var f = new Fixture();
        var ex =
                assertThrows(
                        SecurityException.class,
                        () -> f.engine.dryRun(f.unknownEntityRequest("alice", null)));
        assertEquals(GATE_MESSAGE, ex.getMessage());
    }

    @Test
    void wrongSubjectProbeOfUnknownEntityGetsGenericDenial() throws Exception {
        var f = new Fixture();
        var ex =
                assertThrows(
                        SecurityException.class,
                        () -> f.engine.dryRun(f.unknownEntityRequest("mallory", "tenant-a")));
        assertEquals(GATE_MESSAGE, ex.getMessage());
    }

    @Test
    void unverifiableWarrantProbeNeverReachesResolver() throws Exception {
        var f = new Fixture();
        var forged = f.request(f.sign("alice", "tenant-a", f.otherKey.getPrivate()), "alice", "tenant-a");
        var ex = assertThrows(RuntimeException.class, () -> f.engine.dryRun(forged));
        assertNotEquals(GATE_MESSAGE, ex.getMessage());
        assertFalse(String.valueOf(ex.getMessage()).contains(RESOLVER_MARKER));
    }

    @Test
    void sameTenantCallerPassesGateAndReachesResolver() throws Exception {
        var f = new Fixture();
        var ex =
                assertThrows(
                        RuntimeException.class,
                        () -> f.engine.dryRun(f.unknownEntityRequest("alice", "tenant-a")));
        assertNotEquals(GATE_MESSAGE, ex.getMessage());
        assertTrue(String.valueOf(ex.getMessage()).contains(RESOLVER_MARKER));
    }

    @Test
    void intentPathIsGatedBeforeNameResolution() throws Exception {
        var f = new Fixture();
        var intent =
                new ReadIntent(
                        PROBE,
                        List.of(),
                        null,
                        List.of(),
                        null,
                        null,
                        null,
                        f.context("alice", "tenant-b", f.sign("alice", "tenant-a", f.key.getPrivate())));
        var ex = assertThrows(SecurityException.class, () -> f.engine.executeAsync(intent, null, null));
        assertEquals(GATE_MESSAGE, ex.getMessage());
    }

    @Test
    void approvedExecutionPathIsGatedBeforeNameResolution() throws Exception {
        var f = new Fixture();
        var request = f.unknownEntityRequest("alice", "tenant-b");
        var approval =
                new PlanApproval(
                        request,
                        "approval-1",
                        "fp",
                        f.engine.describeVersionSet().semanticModelVersion(),
                        f.engine.describeVersionSet().capabilityContractVersion(),
                        f.engine.describeVersionSet().capabilityVersion(),
                        f.engine.describeVersionSet().intentVersion(),
                        f.engine.describeVersionSet().planVersion(),
                        "human@example",
                        Instant.now());
        var ex = assertThrows(SecurityException.class, () -> f.engine.executeApprovedAsync(approval));
        assertEquals(GATE_MESSAGE, ex.getMessage());
    }

    @Test
    void validSameTenantRequestPlansAndExecutes() throws Exception {
        var f = new Fixture();
        var request = f.knownEntityRequest("alice", "tenant-a");
        assertNotNull(f.engine.dryRun(request));
        var result = f.engine.executeAsync(request).toCompletableFuture().join();
        assertNotNull(result);
        assertEquals(1, f.provider.count.get());
    }

    @Test
    void wrongTenantCallerOnKnownEntityIsDeniedBeforeAnyExecution() throws Exception {
        var f = new Fixture();
        var request = f.knownEntityRequest("alice", "tenant-b");
        var ex = assertThrows(SecurityException.class, () -> f.engine.dryRun(request));
        assertEquals(GATE_MESSAGE, ex.getMessage());
        assertThrows(SecurityException.class, () -> f.engine.executeAsync(request).toCompletableFuture().join());
        assertEquals(0, f.provider.count.get());
    }

    private static final class Fixture {
        final KeyPair key = newKey();
        final KeyPair otherKey = newKey();
        final FoundgineEngine engine;
        final CountingProvider provider = new CountingProvider();

        Fixture() throws Exception {
            var model =
                    new SemanticModelBuilder()
                            .entity(
                                    EntityId.create("Customer"),
                                    "Customer",
                                    e ->
                                            e.identity(FieldId.create("Customer", "Id"), "Id")
                                                    .field(
                                                            FieldId.create("Customer", "Name"),
                                                            "Name",
                                                            String.class))
                            .build();
            ISecurityWarrantKeyResolver resolver = keyId -> (RSAPublicKey) key.getPublic();
            var options =
                    new FoundgineOptions()
                            .model(model)
                            .authorizationPolicy(new AllowAllSemanticAuthorizationPolicy())
                            .warrantKeyResolver(resolver)
                            .warrantReplayStore(new MemorySecurityWarrantReplayStore())
                            .expectedWarrantIssuer("issuer");
            engine = new FoundgineEngine(options, new TestCompiler(), provider);
        }

        SemanticRequest unknownEntityRequest(String subject, String tenant) {
            var warrant = sign("alice", "tenant-a", key.getPrivate());
            return request(warrant, subject, tenant);
        }

        SemanticRequest knownEntityRequest(String subject, String tenant) {
            var grants = new java.util.ArrayList<CapabilityGrant>();
            for (var c : engine.describeCapabilityContract().capabilities())
                if ("read".equals(c.operation()))
                    grants.add(new CapabilityGrant(c.id(), "read", List.of()));
            var warrant = signWithGrants("alice", "tenant-a", grants, key.getPrivate());
            return new SemanticRequest(
                    EntityId.create("Customer"),
                    List.of(new SemanticSelection(FieldId.create("Customer", "Id"), null, List.of())),
                    null,
                    context(subject, tenant, warrant));
        }

        SemanticRequest request(SecurityWarrant warrant, String subject, String tenant) {
            return new SemanticRequest(
                    EntityId.create(PROBE),
                    List.of(new SemanticSelection(FieldId.create(PROBE, "Id"), null, List.of())),
                    null,
                    context(subject, tenant, warrant));
        }

        SecurityExecutionContext context(String subject, String tenant, SecurityWarrant warrant) {
            return new SecurityExecutionContext(warrant, subject, "api", tenant, tenant);
        }

        SecurityWarrant sign(String subject, String tenant, java.security.PrivateKey signer) {
            return signWithGrants(
                    subject,
                    tenant,
                    List.of(new CapabilityGrant("Customer.read", "read", List.of())),
                    signer);
        }

        SecurityWarrant signWithGrants(
                String subject,
                String tenant,
                List<CapabilityGrant> grants,
                java.security.PrivateKey signer) {
            var now = Instant.now();
            return SecurityWarrantSigner.sign(
                    new SecurityWarrant(
                            "w-1",
                            "issuer",
                            subject,
                            "api",
                            grants,
                            new SecurityWarrantConstraints(
                                    List.of(tenant), null, null, null, null, null),
                            now.minusSeconds(60),
                            now.plusSeconds(600),
                            "nonce-1",
                            "key-1",
                            null,
                            new byte[0],
                            null,
                            List.of()),
                    signer);
        }

        private static KeyPair newKey() {
            try {
                var g = KeyPairGenerator.getInstance("RSA");
                g.initialize(2048);
                return g.generateKeyPair();
            } catch (Exception e) {
                throw new IllegalStateException(e);
            }
        }
    }

    private static final class TestCompiler
            implements IProviderPlanCompiler,
                    IProviderSecurityConformanceEvaluator,
                    ISecurityInvariantProviderCompiler {
        @Override
        public ProviderPlan compile(ExecutionIR ir) {
            return new TestPlan();
        }

        @Override
        public java.util.Collection<String> preservedSecurityInvariants() {
            return SecurityInvariantRegistry.allInvariants().stream().map(x -> x.id()).toList();
        }

        @Override
        public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
            var required = ir.requiredSecurityInvariants();
            return new ProviderSecurityConformanceResult(
                    plan.provider(), required, required, List.of());
        }
    }

    private static final class TestPlan extends ProviderPlan {
        TestPlan() {
            super("test");
        }
    }

    private static final class CountingProvider implements IExecutionProvider {
        final java.util.concurrent.atomic.AtomicInteger count =
                new java.util.concurrent.atomic.AtomicInteger();

        @Override
        public java.util.concurrent.CompletionStage<ExecutionResult> executeAsync(
                ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken) {
            count.incrementAndGet();
            var evidence =
                    com.foundgine.core.execution.ExecutionEvidenceFactory.create(
                            "test", "placeholder", List.of(1), 1, 0, null);
            return CompletableFuture.completedFuture(
                    new ExecutionResult(
                            List.of(new ExecutionRow(java.util.Map.of("Id", 1))),
                            null,
                            evidence,
                            null));
        }
    }
}
