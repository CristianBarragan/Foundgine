package com.foundgine.providers.gateway.execution;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.approvals.FoundgineGatewayApprovalService;
import com.foundgine.providers.gateway.approvals.InMemoryFoundgineGatewayApprovalStore;
import com.foundgine.runtime.*;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;

class GatewayExecutionAndApprovalParityTest {
    static SecurityExecutionContext ctx(String subject, String tenant) {
        var warrant = SecurityWarrant.ofDefaults("w", "iss", subject, "aud", List.of(),
                SecurityWarrantConstraints.UNRESTRICTED, Instant.EPOCH, Instant.MAX, "n", "k", null, null);
        return new SecurityExecutionContext(warrant, subject, "aud", tenant, null);
    }
    static SemanticMutationOperationGraph graph() {
        return new SemanticMutationOperationGraph(List.of(new SemanticMutationOperation(
                new EntityId(1L), SemanticMutationKind.CREATE, null, null, null, null, null, null)));
    }

    /** Records what the gateway handed to the runtime; behaviour is scripted per test. */
    static final class Fakes implements IFoundgineExecutor, IFoundgineMutations {
        SecurityExecutionContext seenReadSecurity, seenMutationSecurity, seenApprovedSecurity;
        RuntimeException syncFailure; Throwable asyncFailure;
        @Override public CompletionStage<ExecutionResult> executeAsync(SemanticRequest r, ExecutionContext c, CancellationToken t) { throw new UnsupportedOperationException(); }
        @Override public CompletionStage<ExecutionResult> executeAsync(ReadIntent i, ExecutionContext c, CancellationToken t) {
            seenReadSecurity = i.security();
            if (syncFailure != null) throw syncFailure;
            return asyncFailure != null ? CompletableFuture.failedFuture(asyncFailure) : CompletableFuture.completedFuture(new ExecutionResult(List.of()));
        }
        @Override public MutationDryRunResult dryRun(SemanticMutationRequest r) { seenMutationSecurity = r.security(); return new MutationDryRunResult("fp", List.of(), List.of()); }
        @Override public CompletionStage<MutationExecutionResult> executeAsync(SemanticMutationRequest r, ExecutionContext c, CancellationToken t) {
            seenMutationSecurity = r.security();
            return asyncFailure != null ? CompletableFuture.failedFuture(asyncFailure) : CompletableFuture.completedFuture(result("fp"));
        }
        @Override public MutationPlanApproval approve(SemanticMutationRequest r, String by) {
            return new MutationPlanApproval(r, "id", "fp", by, Instant.EPOCH);
        }
        @Override public CompletionStage<MutationExecutionResult> executeApprovedAsync(MutationPlanApproval a, ExecutionContext c, CancellationToken t) {
            seenApprovedSecurity = a.request().security();
            if (syncFailure != null) throw syncFailure;
            return CompletableFuture.completedFuture(result(a.planFingerprint()));
        }
        static MutationExecutionResult result(String fp) { return new MutationExecutionResult(new MutationBatchResult(List.of()), fp, "rf", null, null); }
    }

    static final class Events { final List<FoundgineGatewayEventType> types = new ArrayList<>(); boolean failNotifications;
        void publish(FoundgineGatewayEventType t, String s, String c, String p, java.util.Map<String, String> a) {
            if (failNotifications && t != FoundgineGatewayEventType.EXECUTION_STARTED) throw new IllegalStateException("sink down");
            types.add(t); } }

    private static ReadIntent intentWithForgedSecurity() {
        return new ReadIntent("Order", List.of(), null, List.of(), null, null, null, ctx("attacker", "evil"));
    }

    @Test void readOverwritesCallerSuppliedSecurityWithHostContext() throws Exception {
        var f = new Fakes(); var ev = new Events();
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("alice", "t1"), ev::publish);
        exec.executeReadAsync(intentWithForgedSecurity()).toCompletableFuture().get();
        assertEquals("alice", f.seenReadSecurity.subject());
        assertEquals("t1", f.seenReadSecurity.tenant());
        assertEquals(List.of(FoundgineGatewayEventType.EXECUTION_STARTED, FoundgineGatewayEventType.EXECUTION_COMPLETED), ev.types);
    }

    @Test void missingHostContextFailsClosedWithoutEvents() {
        var f = new Fakes(); var ev = new Events();
        var exec = new FoundgineGatewayExecution(f, f, () -> null, ev::publish);
        var ex = assertThrows(java.util.concurrent.ExecutionException.class,
                () -> exec.executeReadAsync(intentWithForgedSecurity()).toCompletableFuture().get());
        assertTrue(ex.getCause() instanceof SecurityException);
        assertTrue(ev.types.isEmpty());
        assertThrows(SecurityException.class, () -> exec.dryRunMutation(graph()));
        assertThrows(SecurityException.class, () -> exec.approveMutation(graph()));
    }

    @Test void approverIdentityComesFromHostSubject() {
        var f = new Fakes();
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("alice", "t1"), new Events()::publish);
        var approval = exec.approveMutation(graph());
        assertEquals("alice", approval.approvedBy());
        assertEquals("alice", approval.request().security().subject());
    }

    @Test void securityExceptionIsReportedAsAuthorizationDenied() {
        var f = new Fakes(); var ev = new Events(); f.asyncFailure = new SecurityException("denied");
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("alice", "t1"), ev::publish);
        assertThrows(java.util.concurrent.ExecutionException.class, () -> exec.executeMutationAsync(graph()).toCompletableFuture().get());
        assertEquals(List.of(FoundgineGatewayEventType.EXECUTION_STARTED, FoundgineGatewayEventType.AUTHORIZATION_DENIED), ev.types);
    }

    @Test void synchronousEngineFailureIsStillReportedAndSurfacedAsFailedStage() {
        var f = new Fakes(); var ev = new Events(); f.syncFailure = new IllegalStateException("plan changed");
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("alice", "t1"), ev::publish);
        var approval = new MutationPlanApproval(new SemanticMutationRequest(graph(), null), "id", "fp", "x", Instant.EPOCH);
        var ex = assertThrows(java.util.concurrent.ExecutionException.class,
                () -> exec.executeApprovedMutationAsync(approval).toCompletableFuture().get());
        assertEquals("plan changed", ex.getCause().getMessage());
        assertEquals(FoundgineGatewayEventType.EXECUTION_FAILED, ev.types.getLast());
    }

    @Test void approvedExecutionRebindsToCurrentCaller() throws Exception {
        var f = new Fakes();
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("bob", "t1"), new Events()::publish);
        var approval = new MutationPlanApproval(new SemanticMutationRequest(graph(), ctx("alice", "t1")), "id", "fp", "alice", Instant.EPOCH);
        exec.executeApprovedMutationAsync(approval).toCompletableFuture().get();
        assertEquals("bob", f.seenApprovedSecurity.subject());
    }

    @Test void notificationFailureNeverMasksCommittedOutcome() throws Exception {
        var f = new Fakes(); var ev = new Events(); ev.failNotifications = true;
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("alice", "t1"), ev::publish);
        assertNotNull(exec.executeMutationAsync(graph()).toCompletableFuture().get());
    }

    @Test void startEventFailureBlocksExecution() {
        var f = new Fakes();
        var exec = new FoundgineGatewayExecution(f, f, () -> ctx("alice", "t1"), (t, s, c, p, a) -> { throw new IllegalStateException("audit down"); });
        assertThrows(java.util.concurrent.ExecutionException.class, () -> exec.executeMutationAsync(graph()).toCompletableFuture().get());
        assertNull(f.seenMutationSecurity);
    }

    // ---- approvals ----
    static final class MutableClock extends Clock {
        Instant now = Instant.parse("2026-01-01T00:00:00Z");
        public java.time.ZoneId getZone() { return ZoneOffset.UTC; }
        public Clock withZone(java.time.ZoneId z) { return this; }
        public Instant instant() { return now; }
    }

    private static FoundgineGatewayApprovalService service(Fakes f, AtomicReference<SecurityExecutionContext> who, MutableClock clock,
            InMemoryFoundgineGatewayApprovalStore store) {
        var exec = new FoundgineGatewayExecution(f, f, who::get, (t, s, c, p, a) -> { });
        return new FoundgineGatewayApprovalService(exec, store, who::get, clock, Duration.ofMinutes(10));
    }

    @Test void approvalHandleIsOpaqueOneTimeAndOwnerBound() throws Exception {
        var f = new Fakes(); var clock = new MutableClock(); var store = new InMemoryFoundgineGatewayApprovalStore(clock);
        var who = new AtomicReference<>(ctx("alice", "t1"));
        var svc = service(f, who, clock, store);
        var receipt = svc.create(graph());
        assertEquals(43, receipt.handle().length());
        assertEquals("fp", receipt.planFingerprint());
        assertEquals(clock.now.plus(Duration.ofMinutes(10)), receipt.expiresAt());

        who.set(ctx("mallory", "t1"));
        assertTrue(svc.executeAsync(receipt.handle()).toCompletableFuture().get().isEmpty());  // other subject
        who.set(ctx("alice", "t2"));
        assertTrue(svc.executeAsync(receipt.handle()).toCompletableFuture().get().isEmpty());  // other tenant

        who.set(ctx("alice", "t1"));
        assertTrue(svc.executeAsync(receipt.handle()).toCompletableFuture().get().isPresent());
        assertTrue(svc.executeAsync(receipt.handle()).toCompletableFuture().get().isEmpty());  // one-time
    }

    @Test void expiredAndRevokedApprovalsAreNotExecutable() throws Exception {
        var f = new Fakes(); var clock = new MutableClock(); var store = new InMemoryFoundgineGatewayApprovalStore(clock);
        var who = new AtomicReference<>(ctx("alice", "t1"));
        var svc = service(f, who, clock, store);
        var expiring = svc.create(graph());
        clock.now = clock.now.plus(Duration.ofMinutes(10));
        assertTrue(svc.executeAsync(expiring.handle()).toCompletableFuture().get().isEmpty());
        var revoked = svc.create(graph());
        assertTrue(svc.revoke(revoked.handle()));
        assertFalse(svc.revoke(revoked.handle()));
        assertTrue(svc.executeAsync(revoked.handle()).toCompletableFuture().get().isEmpty());
    }

    @Test void malformedHandlesAndBadLifetimeAreRejected() {
        var f = new Fakes(); var clock = new MutableClock(); var store = new InMemoryFoundgineGatewayApprovalStore(clock);
        var who = new AtomicReference<>(ctx("alice", "t1"));
        var svc = service(f, who, clock, store);
        assertThrows(IllegalArgumentException.class, () -> svc.revoke("short"));
        assertThrows(IllegalArgumentException.class, () -> svc.revoke("!".repeat(43)));
        var ex = assertThrows(java.util.concurrent.ExecutionException.class, () -> svc.executeAsync(null).toCompletableFuture().get());
        assertTrue(ex.getCause() instanceof IllegalArgumentException);
        var exec = new FoundgineGatewayExecution(f, f, who::get, (t, s, c, p, a) -> { });
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewayApprovalService(exec, store, who::get, clock, Duration.ZERO));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewayApprovalService(exec, store, who::get, clock, Duration.ofHours(25)));
    }

    @Test void concurrentTakersOnlyOneWins() throws Exception {
        var clock = new MutableClock(); var store = new InMemoryFoundgineGatewayApprovalStore(clock);
        var approval = new MutationPlanApproval(new SemanticMutationRequest(graph(), null), "id", "fp", "a", Instant.EPOCH);
        store.store("D", approval, "alice", "t1", clock.now.plusSeconds(60));
        var wins = new java.util.concurrent.atomic.AtomicInteger();
        var threads = new ArrayList<Thread>();
        for (int i = 0; i < 16; i++) { var t = new Thread(() -> { if (store.take("D", "alice", "t1", clock.now).isPresent()) wins.incrementAndGet(); }); threads.add(t); t.start(); }
        for (var t : threads) t.join();
        assertEquals(1, wins.get());
    }
}
