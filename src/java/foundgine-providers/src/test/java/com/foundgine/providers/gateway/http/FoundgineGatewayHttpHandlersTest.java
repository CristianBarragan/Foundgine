package com.foundgine.providers.gateway.http;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.CancellationTokenSource;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.providers.gateway.FoundgineGateway;
import com.foundgine.providers.gateway.FoundgineGatewayCapability;
import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.FoundgineGatewayOptions;
import com.foundgine.providers.gateway.approvals.FoundgineGatewayApprovalService;
import com.foundgine.providers.gateway.approvals.InMemoryFoundgineGatewayApprovalStore;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventHub;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventStreamOptions;
import com.foundgine.providers.gateway.events.FoundgineGatewayScopeDigester;
import com.foundgine.providers.gateway.execution.IFoundgineGatewayExecution;
import com.foundgine.providers.gateway.semantics.FoundgineGatewaySemanticDescription;
import com.foundgine.providers.json.FoundgineJson;
import com.foundgine.runtime.MutationDryRunResult;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.runtime.MutationPlanApproval;
import com.foundgine.runtime.MutationPlanOperation;
import com.foundgine.runtime.SemanticMutationRequest;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.BooleanSupplier;
import org.junit.jupiter.api.Test;

class FoundgineGatewayHttpHandlersTest {

    // ---- fakes -----------------------------------------------------------------------------------------------

    static SecurityExecutionContext ctx(String subject, String tenant) {
        var warrant = SecurityWarrant.ofDefaults("w", "iss", subject, "aud", List.of(),
                SecurityWarrantConstraints.UNRESTRICTED, Instant.EPOCH, Instant.MAX, "n", "k", null, null);
        return new SecurityExecutionContext(warrant, subject, "aud", tenant, null);
    }

    static final class FakeResponse implements GatewayHttpResponse {
        volatile int status = 200;
        volatile boolean committed;
        volatile boolean broken;
        final Map<String, String> headers = new ConcurrentHashMap<>();
        private final ByteArrayOutputStream bytes = new ByteArrayOutputStream();

        @Override public void status(int code) { status = code; }
        @Override public void header(String name, String value) { headers.put(name.toLowerCase(), value); }
        @Override public boolean isCommitted() { return committed; }
        @Override public OutputStream body() {
            committed = true;
            return new OutputStream() {
                @Override public void write(int b) throws IOException { write(new byte[] {(byte) b}, 0, 1); }
                @Override public void write(byte[] b, int off, int len) throws IOException {
                    if (broken) throw new IOException("Broken pipe");
                    synchronized (bytes) { bytes.write(b, off, len); }
                }
            };
        }
        String header(String name) { return headers.get(name.toLowerCase()); }
        String text() { synchronized (bytes) { return bytes.toString(StandardCharsets.UTF_8); } }
    }

    /** Scripted execution; every behaviour can be overridden per test. */
    static final class FakeExecution implements IFoundgineGatewayExecution {
        final AtomicReference<SecurityExecutionContext> who;
        volatile java.util.function.Supplier<CompletionStage<ExecutionResult>> onRead =
                () -> CompletableFuture.completedFuture(new ExecutionResult(List.of()));
        volatile RuntimeException dryRunFailure;
        volatile boolean approvedUnavailable;
        FakeExecution(AtomicReference<SecurityExecutionContext> who) { this.who = who; }

        @Override public CompletionStage<ExecutionResult> executeReadAsync(ReadIntent i, ExecutionContext c, CancellationToken t) { return onRead.get(); }
        @Override public MutationDryRunResult dryRunMutation(SemanticMutationOperationGraph g) {
            if (dryRunFailure != null) throw dryRunFailure;
            return new MutationDryRunResult("fp-1", List.of(new MutationPlanOperation(0, "Customer", "create", List.of("Name"), List.of())), List.of("writes Customer"));
        }
        @Override public MutationPlanApproval approveMutation(SemanticMutationOperationGraph g) {
            return new MutationPlanApproval(new SemanticMutationRequest(g, who.get()), "appr-1", "fp-1", who.get().subject(), Instant.EPOCH);
        }
        @Override public CompletionStage<MutationExecutionResult> executeMutationAsync(SemanticMutationOperationGraph g, ExecutionContext c, CancellationToken t) { throw new UnsupportedOperationException(); }
        @Override public CompletionStage<MutationExecutionResult> executeApprovedMutationAsync(MutationPlanApproval a, ExecutionContext c, CancellationToken t) {
            return CompletableFuture.completedFuture(new MutationExecutionResult(new MutationBatchResult(List.of()), a.planFingerprint(), "rf-1", a.approvalId(), a.approvedBy()));
        }
    }

    static final class Env {
        final AtomicReference<SecurityExecutionContext> who = new AtomicReference<>(ctx("alice", "t1"));
        final FoundgineGatewayOptions options = new FoundgineGatewayOptions().addCapability(new FoundgineGatewayCapability(
                "cap.1", "Cap \"one\"", "1.0", "tool", "line1\nline2", List.of("read", "write"), Map.of("b", "2", "a", "1")));
        final FoundgineGatewayEventHub hub = new FoundgineGatewayEventHub(options);
        final FoundgineGateway gateway = new FoundgineGateway(options, hub);
        final FakeExecution execution = new FakeExecution(who);
        final FoundgineGatewayHttpOptions limits = new FoundgineGatewayHttpOptions();
        final FoundgineGatewayEventStreamOptions stream = new FoundgineGatewayEventStreamOptions().heartbeatInterval(Duration.ofMillis(60));
        final AtomicReference<Boolean> operator = new AtomicReference<>(false);
        java.util.function.Function<FoundgineGatewaySemanticDescription, String> semanticJson = d -> "{\"semantic\":true}";
        GatewayHttpRouter router;

        Env() { rebuild(); }

        Env rebuild() {
            var approvals = new FoundgineGatewayApprovalService(execution, new InMemoryFoundgineGatewayApprovalStore(), who::get);
            var services = FoundgineGatewayHttpServices.builder().gateway(gateway)
                    .semanticSource(() -> new FoundgineGatewaySemanticDescription(null, null)).semanticJson(semanticJson)
                    .execution(execution).approvals(approvals).hub(hub).streamOptions(stream).httpOptions(limits)
                    .security(who::get).authorizer(new GatewayHttpAuthorizer() {
                        @Override public Decision authorize(GatewayHttpRequest r) {
                            var user = r.header("X-User");
                            if (user == null) return Decision.UNAUTHENTICATED;
                            return "denied".equals(user) ? Decision.FORBIDDEN : Decision.ALLOWED;
                        }
                        @Override public boolean satisfiesPolicy(GatewayHttpRequest r, String policy) { return operator.get(); }
                    }).build();
            router = FoundgineGatewayRoutes.map(services);
            return this;
        }

        String owner(String tenant, String subject) { return new FoundgineGatewayScopeDigester(stream).owner(tenant, subject); }

        FakeResponse send(String method, String path, String body, String... headerPairs) {
            var builder = GatewayHttpRequest.builder(method, path);
            boolean overridesUser = false;
            for (int i = 0; i < headerPairs.length; i += 2) overridesUser |= headerPairs[i].equalsIgnoreCase("X-User");
            if (!overridesUser) builder.header("X-User", "alice");
            if (body != null) builder.body(body.getBytes(StandardCharsets.UTF_8));
            for (int i = 0; i < headerPairs.length; i += 2) builder.header(headerPairs[i], headerPairs[i + 1]);
            var res = new FakeResponse();
            router.dispatch(builder.build(), res);
            return res;
        }

        List<FoundgineGatewayEvent> events() { return hub.eventStore().readAfter(0, 1000); }
    }

    static Map<?, ?> json(String text) { return (Map<?, ?>) FoundgineJson.parse(text); }

    static void await(BooleanSupplier condition) throws InterruptedException {
        long deadline = System.nanoTime() + Duration.ofSeconds(5).toNanos();
        while (!condition.getAsBoolean()) {
            if (System.nanoTime() > deadline) fail("Timed out waiting for condition");
            Thread.sleep(5);
        }
    }

    static final String READ = "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Name\"}]}";
    static final String CREATE = "{\"operations\":[{\"entityId\":1,\"kind\":\"create\",\"fields\":{\"7\":\"x\"}}]}";

    // ---- router ----------------------------------------------------------------------------------------------

    @Test void unknownPathIs404AndWrongMethodIs405WithAllow() {
        var env = new Env();
        assertEquals(404, env.send("GET", "/nope", null).status);
        assertEquals(404, env.send("GET", "/foundgine/capabilities/a/b", null).status);
        var wrong = env.send("PUT", "/foundgine/capabilities", null);
        assertEquals(405, wrong.status);
        assertEquals("GET", wrong.header("Allow"));
        assertEquals("DELETE", env.send("PATCH", "/foundgine/mutations/approvals/x", null).header("Allow"));
        assertEquals("POST", env.send("GET", "/foundgine/read", null).header("Allow"));
    }

    @Test void protectedRoutesMapAuthenticationDecisionsTo401And403() {
        var env = new Env();
        var anonymous = new FakeResponse();
        env.router.dispatch(GatewayHttpRequest.builder("GET", "/foundgine/capabilities").build(), anonymous);
        assertEquals(401, anonymous.status);
        assertEquals(403, env.send("GET", "/foundgine/capabilities", null, "X-User", "denied").status);
    }

    @Test void routerRefusesProtectedRoutesWithoutAnAuthorizer() {
        var route = new GatewayHttpRoute("GET", "/x", true, (q, r) -> { });
        assertThrows(IllegalStateException.class, () -> new GatewayHttpRouter(List.of(route), null));
        assertDoesNotThrow(() -> new GatewayHttpRouter(List.of(new GatewayHttpRoute("GET", "/x", false, (q, r) -> { })), null));
    }

    @Test void discoveryCanBePublicButExecutionStaysProtected() {
        var env = new Env();
        var services = FoundgineGatewayHttpServices.builder().gateway(env.gateway)
                .semanticSource(() -> null).execution(env.execution)
                .approvals(new FoundgineGatewayApprovalService(env.execution, new InMemoryFoundgineGatewayApprovalStore(), env.who::get))
                .hub(env.hub).security(env.who::get).authorizer(r -> GatewayHttpAuthorizer.Decision.UNAUTHENTICATED).build();
        var router = FoundgineGatewayRoutes.map(services, "/g/", false);
        var open = new FakeResponse();
        router.dispatch(GatewayHttpRequest.builder("GET", "/g/capabilities").build(), open);
        assertEquals(200, open.status);
        var read = new FakeResponse();
        router.dispatch(GatewayHttpRequest.builder("POST", "/g/read").body("{}".getBytes()).build(), read);
        assertEquals(401, read.status);
    }

    @Test void routePrefixMustStartWithSlash() {
        var env = new Env();
        var services = FoundgineGatewayHttpServices.builder().gateway(env.gateway).semanticSource(() -> null)
                .execution(env.execution).approvals(new FoundgineGatewayApprovalService(env.execution, new InMemoryFoundgineGatewayApprovalStore(), env.who::get))
                .hub(env.hub).security(env.who::get).authorizer(r -> GatewayHttpAuthorizer.Decision.ALLOWED).build();
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayRoutes.map(services, "foundgine", true));
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayRoutes.map(services, " ", true));
    }

    @Test void pathParametersArePercentDecodedAndMalformedEscapesDoNotMatch() {
        assertEquals(Map.of("id", "a b/é"), GatewayHttpRouter.match("/c/{id}", "/c/a%20b%2F%C3%A9"));
        assertEquals(Map.of("id", "a+b"), GatewayHttpRouter.match("/c/{id}", "/c/a+b"));
        assertNull(GatewayHttpRouter.match("/c/{id}", "/c/%zz"));
        assertNull(GatewayHttpRouter.match("/c/{id}", "/c/%"));
        assertNull(GatewayHttpRouter.match("/c/{id}", "/c/%C3"), "invalid UTF-8");
        assertNull(GatewayHttpRouter.match("/c/{id}", "/c/"));
    }

    @Test void escapedHandlerFailureBecomes500WithoutDetail() {
        var routes = List.of(new GatewayHttpRoute("GET", "/boom", false, (q, r) -> { throw new IllegalStateException("secret detail"); }));
        var seen = new AtomicReference<RuntimeException>();
        var router = new GatewayHttpRouter(routes, null).onUnhandled(seen::set);
        var res = new FakeResponse();
        router.dispatch(GatewayHttpRequest.builder("GET", "/boom").build(), res);
        assertEquals(500, res.status);
        assertEquals("", res.text());
        assertEquals("secret detail", seen.get().getMessage());
    }

    // ---- discovery -------------------------------------------------------------------------------------------

    @Test void capabilitiesAreWrittenCamelCaseWithEscapingAndSortedMetadata() {
        var res = new Env().send("GET", "/foundgine/capabilities", null);
        assertEquals(200, res.status);
        assertEquals("application/json; charset=utf-8", res.header("Content-Type"));
        assertEquals("{\"name\":\"Foundgine Gateway\",\"contractVersion\":\"1.0\",\"capabilities\":[{\"id\":\"cap.1\",\"name\":\"Cap \\\"one\\\"\","
                + "\"version\":\"1.0\",\"kind\":\"tool\",\"description\":\"line1\\nline2\",\"operations\":[\"read\",\"write\"],\"metadata\":{\"a\":\"1\",\"b\":\"2\"}}]}", res.text());
    }

    @Test void capabilityLookupIs200Or404() {
        var env = new Env();
        assertEquals(200, env.send("GET", "/foundgine/capabilities/cap.1", null).status);
        assertEquals("cap.1", json(env.send("GET", "/foundgine/capabilities/CAP.1", null).text()).get("id"));
        assertEquals(404, env.send("GET", "/foundgine/capabilities/missing", null).status);
    }

    @Test void semanticEndpointUsesTheConfiguredSerializer() {
        var res = new Env().send("GET", "/foundgine/semantic", null);
        assertEquals(200, res.status);
        assertEquals("{\"semantic\":true}", res.text());
    }

    @Test void defaultSemanticSerializerWritesTheCoreContractWithJackson() {
        var text = FoundgineGatewaySemanticJson.toJson(new FoundgineGatewaySemanticDescription(
                new SemanticCapabilityContract(1, List.of()), new SemanticVersionSet("sha256:abc", 1, 2, 3, 4)));
        var root = json(text);
        assertEquals(1, ((Number) ((Map<?, ?>) root.get("contract")).get("version")).intValue());
        assertEquals("sha256:abc", ((Map<?, ?>) root.get("versions")).get("semanticModelVersion"));
        assertEquals(4, ((Number) ((Map<?, ?>) root.get("versions")).get("planVersion")).intValue());
    }

    // ---- read ------------------------------------------------------------------------------------------------

    @Test void readExecutesAndJournalsStartedThenCompletedWithOwnerDigest() {
        var env = new Env();
        var res = env.send("POST", "/foundgine/read", READ);
        assertEquals(200, res.status);
        assertEquals("{\"rows\":[],\"pageInfo\":null}", res.text());
        var id = res.header("X-Foundgine-Execution-Id");
        assertTrue(id.matches("[0-9a-f]{32}"));
        var events = env.events();
        assertEquals(List.of(FoundgineGatewayEventType.EXECUTION_STARTED, FoundgineGatewayEventType.EXECUTION_COMPLETED),
                events.stream().map(FoundgineGatewayEvent::type).toList());
        assertEquals(id, events.get(0).correlationId());
        assertEquals("execution", events.get(0).subject());
        assertEquals("read", events.get(0).attributes().get("operation.kind"));
        assertEquals(env.owner("t1", "alice"), events.get(0).attributes().get("owner.digest"));
        assertEquals("succeeded", events.get(1).attributes().get("outcome.code"));
    }

    @Test void readRejectsBadBodiesWith400() {
        var env = new Env();
        assertEquals(400, env.send("POST", "/foundgine/read", "").status);
        assertEquals(400, env.send("POST", "/foundgine/read", "   ").status);
        assertEquals(400, env.send("POST", "/foundgine/read", "{not json").status);
        assertEquals(400, env.send("POST", "/foundgine/read", "{\"rootEntity\":\"C\",\"bogus\":1}").status, "unknown members are rejected");
        assertEquals(400, env.send("POST", "/foundgine/read", "{\"rootEntity\":\"C\",\"limit\":999999}").status, "limit above the maximum");
        assertTrue(env.events().isEmpty(), "invalid requests never start an execution");

        var notUtf8 = new FakeResponse();
        env.router.dispatch(GatewayHttpRequest.builder("POST", "/foundgine/read").header("X-User", "alice")
                .body(new byte[] {'{', (byte) 0xC3, '}'}).build(), notUtf8);
        assertEquals(400, notUtf8.status);
    }

    @Test void readEnforcesTheBodyLimitByDeclaredLengthAndWhileStreaming() {
        var env = new Env();
        env.limits.maxRequestBytes = 64;
        var big = "{\"rootEntity\":\"" + "x".repeat(100) + "\"}";
        assertEquals(413, env.send("POST", "/foundgine/read", big).status, "declared Content-Length");

        var unknownLength = new FakeResponse();
        env.router.dispatch(GatewayHttpRequest.builder("POST", "/foundgine/read").header("X-User", "alice")
                .body(new ByteArrayInputStream(big.getBytes(StandardCharsets.UTF_8)), -1).build(), unknownLength);
        assertEquals(413, unknownLength.status, "chunked bodies are capped while reading");
        assertTrue(env.events().isEmpty());
    }

    @Test void readMapsAuthorizationFailureTo403AndJournalsAuthorizationDenied() {
        var env = new Env();
        env.execution.onRead = () -> CompletableFuture.failedFuture(new SecurityException("no"));
        assertEquals(403, env.send("POST", "/foundgine/read", READ).status);
        var events = env.events();
        assertEquals(FoundgineGatewayEventType.EXECUTION_FAILED, events.get(1).type());
        assertEquals("authorization_denied", events.get(1).attributes().get("outcome.code"));
    }

    @Test void readWithoutATrustedSecurityContextFailsClosedBeforeExecuting() {
        var env = new Env();
        var called = new AtomicReference<>(false);
        env.execution.onRead = () -> { called.set(true); return CompletableFuture.completedFuture(new ExecutionResult(List.of())); };
        env.who.set(null);
        assertEquals(403, env.send("POST", "/foundgine/read", READ).status);
        assertFalse(called.get());
        assertTrue(env.events().isEmpty());
    }

    @Test void readFailureIsJournaledAsFailedAndSurfacesAs500() {
        var env = new Env();
        var seen = new AtomicReference<RuntimeException>();
        env.router.onUnhandled(seen::set);
        env.execution.onRead = () -> CompletableFuture.failedFuture(new IllegalStateException("engine down"));
        var res = env.send("POST", "/foundgine/read", READ);
        assertEquals(500, res.status);
        assertEquals("", res.text(), "no exception detail reaches the caller");
        assertEquals("failed", env.events().get(1).attributes().get("outcome.code"));
        assertEquals("engine down", seen.get().getMessage());
    }

    @Test void readResultWithoutAWireMappingIs501() {
        var env = new Env();
        env.execution.onRead = () -> CompletableFuture.completedFuture(
                new ExecutionResult(List.of(new ExecutionRow(Map.of("v", new Object())))));
        assertEquals(501, env.send("POST", "/foundgine/read", READ).status);
    }

    @Test void readSerializesRowsWithoutReflection() {
        var env = new Env();
        var values = new LinkedHashMap<String, Object>();
        values.put("Name", "Ana \"A\"");
        values.put("Id", new java.math.BigInteger("18446744073709551615"));
        env.execution.onRead = () -> CompletableFuture.completedFuture(new ExecutionResult(List.of(new ExecutionRow(values))));
        assertEquals("{\"rows\":[{\"values\":{\"Name\":\"Ana \\\"A\\\"\",\"Id\":18446744073709551615}}],\"pageInfo\":null}",
                env.send("POST", "/foundgine/read", READ).text());
    }

    // ---- mutations -------------------------------------------------------------------------------------------

    @Test void dryRunReturnsThePlan() {
        var res = new Env().send("POST", "/foundgine/mutations/dry-run", CREATE);
        assertEquals(200, res.status);
        assertEquals("{\"planFingerprint\":\"fp-1\",\"operations\":[{\"index\":0,\"entity\":\"Customer\",\"kind\":\"create\","
                + "\"fields\":[\"Name\"],\"returnFields\":[]}],\"effects\":[\"writes Customer\"]}", res.text());
    }

    @Test void dryRunMapsClientAndAuthorizationErrors() {
        var env = new Env();
        assertEquals(400, env.send("POST", "/foundgine/mutations/dry-run", "{\"operations\":[]}").status);
        assertEquals(400, env.send("POST", "/foundgine/mutations/dry-run", "{}").status);
        env.execution.dryRunFailure = new SecurityException("no");
        assertEquals(403, env.send("POST", "/foundgine/mutations/dry-run", CREATE).status);
        env.execution.dryRunFailure = new IllegalArgumentException("bad plan");
        assertEquals(400, env.send("POST", "/foundgine/mutations/dry-run", CREATE).status);
        env.limits.maxRequestBytes = 10;
        assertEquals(413, env.send("POST", "/foundgine/mutations/dry-run", CREATE).status);
    }

    @Test void approvalLifecycleCreateExecuteAndReplayIsRejected() {
        var env = new Env();
        var created = env.send("POST", "/foundgine/mutations/approvals", CREATE);
        assertEquals(201, created.status);
        var body = json(created.text());
        var handle = (String) body.get("handle");
        assertEquals(43, handle.length());
        assertEquals("fp-1", body.get("planFingerprint"));
        assertNotNull(Instant.parse((String) body.get("expiresAt")));

        var executed = env.send("POST", "/foundgine/mutations/approvals/" + handle + "/execute", null);
        assertEquals(200, executed.status);
        assertEquals("{\"planFingerprint\":\"fp-1\",\"resultFingerprint\":\"rf-1\",\"approvalId\":\"appr-1\",\"approvedBy\":\"alice\","
                + "\"operationCount\":0,\"totalAffectedRows\":0}", executed.text());
        assertTrue(executed.header("X-Foundgine-Execution-Id").matches("[0-9a-f]{32}"));
        assertEquals(List.of(FoundgineGatewayEventType.EXECUTION_STARTED, FoundgineGatewayEventType.EXECUTION_COMPLETED),
                env.events().stream().map(FoundgineGatewayEvent::type).toList());
        assertEquals("mutation", env.events().get(0).attributes().get("operation.kind"));

        var replay = env.send("POST", "/foundgine/mutations/approvals/" + handle + "/execute", null);
        assertEquals(404, replay.status, "an approval is single-use");
        assertEquals("approval_unavailable", env.events().get(3).attributes().get("outcome.code"));
    }

    @Test void approvalForAnotherCallerIsNotExecutable() {
        var env = new Env();
        var handle = (String) json(env.send("POST", "/foundgine/mutations/approvals", CREATE).text()).get("handle");
        env.who.set(ctx("mallory", "t1"));
        assertEquals(404, env.send("POST", "/foundgine/mutations/approvals/" + handle + "/execute", null).status);
        assertEquals(404, env.send("DELETE", "/foundgine/mutations/approvals/" + handle, null).status);
    }

    @Test void malformedApprovalHandleIs400AndJournaledAsInvalidRequest() {
        var env = new Env();
        assertEquals(400, env.send("POST", "/foundgine/mutations/approvals/short/execute", null).status);
        assertEquals("invalid_request", env.events().get(1).attributes().get("outcome.code"));
        assertEquals(400, env.send("DELETE", "/foundgine/mutations/approvals/short", null).status);
    }

    @Test void revokeIs204ThenNotFound() {
        var env = new Env();
        var handle = (String) json(env.send("POST", "/foundgine/mutations/approvals", CREATE).text()).get("handle");
        assertEquals(204, env.send("DELETE", "/foundgine/mutations/approvals/" + handle, null).status);
        assertEquals(404, env.send("DELETE", "/foundgine/mutations/approvals/" + handle, null).status);
    }

    @Test void approvalCreationWithoutSecurityContextIs403() {
        var env = new Env();
        env.who.set(null);
        assertEquals(403, env.send("POST", "/foundgine/mutations/approvals", CREATE).status);
    }

    // ---- execution status ------------------------------------------------------------------------------------

    @Test void executionStatusReportsOutcomeToTheOwnerOnly() {
        var env = new Env();
        var id = env.send("POST", "/foundgine/read", READ).header("X-Foundgine-Execution-Id");
        var res = env.send("GET", "/foundgine/executions/" + id, null);
        assertEquals(200, res.status);
        var status = json(res.text());
        assertEquals("succeeded", status.get("status"));
        assertEquals("read", status.get("operationKind"));
        assertEquals("succeeded", status.get("outcomeCode"));
        assertNotNull(status.get("startedAt"));
        assertNotNull(status.get("completedAt"));

        env.who.set(ctx("bob", "t1"));
        assertEquals(404, env.send("GET", "/foundgine/executions/" + id, null).status, "someone else's execution looks unknown");
        env.who.set(ctx("alice", "t2"));
        assertEquals(404, env.send("GET", "/foundgine/executions/" + id, null).status, "same subject, different tenant");
    }

    @Test void runningAndFailedExecutionStatuses() {
        var env = new Env();
        var id = "0123456789abcdef0123456789ABCDEF";
        var attrs = new LinkedHashMap<String, String>();
        attrs.put("operation.kind", "mutation");
        attrs.put("owner.digest", env.owner("t1", "alice"));
        env.hub.publish(FoundgineGatewayEventType.EXECUTION_STARTED, "execution", id, null, attrs);
        var running = json(env.send("GET", "/foundgine/executions/" + id, null).text());
        assertEquals("running", running.get("status"));
        assertNull(running.get("completedAt"));
        assertEquals(id, running.get("executionId"));

        attrs.put("outcome.code", "failed");
        env.hub.publish(FoundgineGatewayEventType.EXECUTION_FAILED, "execution", id, null, attrs);
        var failed = json(env.send("GET", "/foundgine/executions/" + id, null).text());
        assertEquals("failed", failed.get("status"));
        assertEquals("failed", failed.get("outcomeCode"));
    }

    @Test void executionStatusValidatesTheIdAndSecurityContext() {
        var env = new Env();
        assertEquals(400, env.send("GET", "/foundgine/executions/not-a-guid", null).status);
        assertEquals(400, env.send("GET", "/foundgine/executions/" + "g".repeat(32), null).status);
        assertEquals(404, env.send("GET", "/foundgine/executions/" + "a".repeat(32), null).status);
        env.who.set(null);
        assertEquals(403, env.send("GET", "/foundgine/executions/" + "a".repeat(32), null).status);
    }

    // ---- SSE -------------------------------------------------------------------------------------------------

    /** Runs the stream on its own thread, as a host would, and returns handles to observe and stop it. */
    static final class Stream {
        final FakeResponse response = new FakeResponse();
        final CancellationTokenSource cancel = new CancellationTokenSource();
        final Thread thread;
        Stream(Env env, String query, String... headerPairs) {
            var builder = GatewayHttpRequest.builder("GET", "/foundgine/events").header("X-User", "alice").cancellation(cancel.token());
            if (query != null) builder.query("after", query);
            for (int i = 0; i < headerPairs.length; i += 2) builder.header(headerPairs[i], headerPairs[i + 1]);
            var request = builder.build();
            thread = new Thread(() -> env.router.dispatch(request, response), "sse-test");
            thread.setDaemon(true);
            thread.start();
        }
        void stop() throws InterruptedException { cancel.cancel(); thread.join(5000); assertFalse(thread.isAlive(), "stream thread must end"); }
    }

    private static void publish(Env env, FoundgineGatewayEventType type, String owner) {
        var attrs = new LinkedHashMap<String, String>();
        attrs.put("operation.kind", "read");
        if (owner != null) attrs.put("owner.digest", owner);
        env.hub.publish(type, "execution", "corr", null, attrs);
    }

    @Test void streamRejectsBadCursorsAndMissingSecurityContext() throws Exception {
        var env = new Env();
        var invalid = env.send("GET", "/foundgine/events", null, "Last-Event-ID", "abc");
        assertEquals(400, invalid.status);
        assertEquals("0", invalid.header("X-Foundgine-Latest-Sequence"));
        env.stream.maxReplayWindow(2);
        for (int i = 0; i < 5; i++) publish(env, FoundgineGatewayEventType.EXECUTION_STARTED, env.owner("t1", "alice"));
        var tooOld = env.send("GET", "/foundgine/events", null, "Last-Event-ID", "0");
        assertEquals(410, tooOld.status);
        assertEquals("5", tooOld.header("X-Foundgine-Latest-Sequence"));
        env.who.set(null);
        assertEquals(403, env.send("GET", "/foundgine/events", null).status);
        assertEquals(0, env.hub.subscriberCount());
    }

    @Test void streamReplaysOnlyVisibleEventsFramesThemAndCheckpointsHiddenOnes() throws Exception {
        var env = new Env();
        publish(env, FoundgineGatewayEventType.EXECUTION_STARTED, env.owner("t1", "alice"));        // 1 visible
        publish(env, FoundgineGatewayEventType.EXECUTION_STARTED, env.owner("t1", "someone-else")); // 2 hidden
        var s = new Stream(env, null, "Last-Event-ID", "0");
        await(() -> s.response.text().contains("id: 1\n") && s.response.text().contains(": keepalive\nid: 2\n\n"));
        var text = s.response.text();
        assertEquals(200, s.response.status);
        assertEquals("text/event-stream; charset=utf-8", s.response.header("Content-Type"));
        assertEquals("no-cache, no-store", s.response.header("Cache-Control"));
        assertEquals("no", s.response.header("X-Accel-Buffering"));
        assertEquals("2", s.response.header("X-Foundgine-Latest-Sequence"));
        assertTrue(text.startsWith("retry: 3000\n\n"));
        assertTrue(text.contains("id: 1\nevent: ExecutionStarted\ndata: {\"sequence\":1,"), text);
        assertFalse(text.contains("id: 2\nevent:"), "the other owner's event is never sent");
        assertFalse(text.contains("owner.digest"), "reserved scope attributes are stripped from the wire");
        assertTrue(text.contains("\"operation.kind\":\"read\""));
        s.stop();
        assertEquals(0, env.hub.subscriberCount());
    }

    @Test void streamDeliversLiveEventsAndTheAfterQueryParameterIsHonoured() throws Exception {
        var env = new Env();
        publish(env, FoundgineGatewayEventType.EXECUTION_STARTED, env.owner("t1", "alice")); // 1
        var s = new Stream(env, "1");                                                         // resume after 1: nothing to replay
        await(() -> s.response.text().startsWith("retry: 3000"));
        assertFalse(s.response.text().contains("event: ExecutionStarted"));
        env.hub.publish(FoundgineGatewayEventType.CAPABILITY_CHANGED, "gateway", null, null, Map.of());
        await(() -> s.response.text().contains("id: 2\nevent: CapabilityChanged\n"));
        s.stop();
    }

    @Test void headerCursorWinsOverQueryParameter() throws Exception {
        var env = new Env();
        for (int i = 0; i < 3; i++) publish(env, FoundgineGatewayEventType.EXECUTION_STARTED, env.owner("t1", "alice"));
        var s = new Stream(env, "3", "Last-Event-ID", "1");
        await(() -> s.response.text().contains("id: 3\n"));
        assertTrue(s.response.text().contains("id: 2\nevent:"), "replay started after 1, not after 3");
        s.stop();
    }

    @Test void operatorPolicyRevealsUnscopedEvents() throws Exception {
        var env = new Env();
        env.stream.operatorPolicy("ops");
        publish(env, FoundgineGatewayEventType.PROVIDER_REGISTERED, null);
        var plain = new Stream(env, null, "Last-Event-ID", "0");
        await(() -> plain.response.text().contains(": keepalive"));
        assertFalse(plain.response.text().contains("ProviderRegistered"));
        plain.stop();

        env.operator.set(true);
        var operator = new Stream(env, null, "Last-Event-ID", "0");
        await(() -> operator.response.text().contains("event: ProviderRegistered"));
        operator.stop();
    }

    @Test void streamSendsPeriodicKeepAlivesEvenWhenEveryEventIsHidden() throws Exception {
        var env = new Env();
        var s = new Stream(env, null);
        await(() -> s.response.text().startsWith("retry: 3000"));
        for (int i = 0; i < 20; i++) {
            publish(env, FoundgineGatewayEventType.EXECUTION_STARTED, env.owner("t1", "someone-else"));
            Thread.sleep(10);
        }
        await(() -> s.response.text().contains("id: 20\n"));
        assertTrue(s.response.text().split(": keepalive", -1).length > 2, "keep-alives continue while hidden events keep arriving");
        s.stop();
    }

    @Test void clientDisconnectEndsTheStreamAndReleasesTheSubscription() throws Exception {
        var env = new Env();
        var s = new Stream(env, null);
        await(() -> s.response.text().startsWith("retry: 3000") && env.hub.subscriberCount() == 1);
        s.response.broken = true; // the next write fails like a dropped socket
        publish(env, FoundgineGatewayEventType.CAPABILITY_CHANGED, null);
        s.thread.join(5000);
        assertFalse(s.thread.isAlive());
        assertEquals(0, env.hub.subscriberCount());
        assertEquals(200, s.response.status, "a dead peer is not reported as a server error");
    }

    @Test void cancellationEndsTheStreamPromptly() throws Exception {
        var env = new Env();
        env.stream.heartbeatInterval(Duration.ofSeconds(30));
        var s = new Stream(env, null);
        await(() -> env.hub.subscriberCount() == 1 && s.response.text().startsWith("retry: 3000"));
        long start = System.nanoTime();
        s.stop();
        assertTrue(Duration.ofNanos(System.nanoTime() - start).toSeconds() < 5, "cancellation must not wait for the heartbeat");
        assertEquals(0, env.hub.subscriberCount());
    }

    // ---- writers ---------------------------------------------------------------------------------------------

    @Test void sseWriterFramesRetryEventsAndKeepAlives() throws Exception {
        var out = new ByteArrayOutputStream();
        var sse = new GatewaySseWriter(out);
        sse.retry(1500);
        sse.event(7, "CapabilityChanged", "{\"a\":1}");
        sse.event(8, "Custom", "line1\nline2");
        sse.keepAlive(null);
        sse.keepAlive(9L);
        assertEquals("retry: 1500\n\n"
                + "id: 7\nevent: CapabilityChanged\ndata: {\"a\":1}\n\n"
                + "id: 8\nevent: Custom\ndata: line1\ndata: line2\n\n"
                + ": keepalive\n\n"
                + ": keepalive\nid: 9\n\n", out.toString(StandardCharsets.UTF_8));
    }

    @Test void jsonWriterWritesNullsExplicitlyAndFormatsTimestamps() {
        var status = new com.foundgine.providers.gateway.execution.FoundgineGatewayExecutionStatus(
                "id", "running", "read", Instant.parse("2026-01-02T03:04:05Z"), null, null);
        assertEquals("{\"executionId\":\"id\",\"status\":\"running\",\"operationKind\":\"read\","
                + "\"startedAt\":\"2026-01-02T03:04:05Z\",\"completedAt\":null,\"outcomeCode\":null}", FoundgineGatewayJsonWriter.executionStatus(status));
    }

    // ---- request ---------------------------------------------------------------------------------------------

    @Test void requestHeadersAreCaseInsensitiveAndPathParamsBind() {
        var request = GatewayHttpRequest.builder("get", "/x").header("Last-Event-ID", "5").query("after", "7").build();
        assertEquals("GET", request.method());
        assertEquals("5", request.header("last-event-id"));
        assertTrue(request.hasHeader("LAST-EVENT-ID"));
        assertEquals("7", request.queryParam("after"));
        assertNull(request.queryParam("missing"));
        assertThrows(IllegalArgumentException.class, () -> GatewayHttpRequest.builder("GET", "x"));
        assertEquals("v", request.withPathParams(Map.of("k", "v")).pathParam("k"));
    }

    @SuppressWarnings("unused") private static final List<Object> KEEP = new ArrayList<>();
    @SuppressWarnings("unused") private static final EntityId ID = new EntityId(1L);
    @SuppressWarnings("unused") private static final SemanticMutationKind KIND = SemanticMutationKind.CREATE;
    @SuppressWarnings("unused") private static final Class<?> OP = SemanticMutationOperation.class;
}
