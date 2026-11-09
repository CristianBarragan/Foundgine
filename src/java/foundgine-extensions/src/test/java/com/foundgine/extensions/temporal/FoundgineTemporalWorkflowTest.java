package com.foundgine.extensions.temporal;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNotSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.extensions.temporal.activities.FoundgineNonRetryableOperationException;
import com.foundgine.extensions.temporal.activities.FoundgineOperationActivities;
import com.foundgine.extensions.temporal.activities.FoundgineOperationContext;
import com.foundgine.extensions.temporal.activities.IFoundgineTemporalOperation;
import com.foundgine.extensions.temporal.configuration.FoundgineTemporalOptions;
import com.foundgine.extensions.temporal.configuration.UnroutedTopicBehavior;
import com.foundgine.extensions.temporal.contracts.FoundgineApprovalDecision;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationResult;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessDefinition;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessState;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessStep;
import com.foundgine.extensions.temporal.hosting.FoundgineProcessCatalog;
import com.foundgine.extensions.temporal.hosting.FoundgineWorkflowStartRequest;
import com.foundgine.extensions.temporal.hosting.FoundgineWorkflowStarter;
import com.foundgine.extensions.temporal.hosting.FoundgineWorkflowStatus;
import com.foundgine.extensions.temporal.hosting.InProcessFoundgineWorkflowEngine;
import com.foundgine.extensions.temporal.messaging.FoundgineTemporalOutboxPublisher;
import com.foundgine.providers.outbox.FoundgineOutboxMessage;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Function;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

class FoundgineTemporalWorkflowTest {
    private static final Duration WAIT = Duration.ofSeconds(10);

    private final List<String> log = Collections.synchronizedList(new ArrayList<>());
    private final AtomicInteger factoryCalls = new AtomicInteger();
    private final List<IFoundgineTemporalOperation> operations = new ArrayList<>();
    private final FoundgineTemporalOptions options = new FoundgineTemporalOptions();
    private final FoundgineProcessCatalog catalog = new FoundgineProcessCatalog();
    private FoundgineOperationActivities activities;
    private InProcessFoundgineWorkflowEngine engine;
    private FoundgineWorkflowStarter starter;

    private IFoundgineTemporalOperation op(String name, Function<FoundgineOperationContext, String> body) {
        return new IFoundgineTemporalOperation() {
            @Override public String name() { return name; }

            @Override
            public FoundgineOperationResult execute(FoundgineOperationContext context, CancellationToken token) {
                log.add(name);
                return new FoundgineOperationResult(body.apply(context));
            }
        };
    }

    private IFoundgineTemporalOperation failing(String name, java.util.function.Supplier<RuntimeException> failure) {
        return op(name, c -> { throw failure.get(); });
    }

    private void start(FoundgineProcessStep... steps) {
        options.activityMaximumAttempts = 3;
        options.activityInitialRetryInterval = Duration.ofMillis(5);
        options.activityMaximumRetryInterval = Duration.ofMillis(10);
        options.activityStartToCloseTimeout = Duration.ofSeconds(5);
        catalog.add(new FoundgineProcessDefinition("order", List.of(steps)));
        activities = new FoundgineOperationActivities(operations, (tenant, initiator, token) -> {
            factoryCalls.incrementAndGet();
            var warrant = SecurityWarrant.ofDefaults(UUID.randomUUID().toString(), "issuer", initiator, "foundgine",
                    List.of(), SecurityWarrantConstraints.UNRESTRICTED, Instant.now(), Instant.now().plusSeconds(60),
                    "nonce", "key", null, new byte[0]);
            return new SecurityExecutionContext(warrant, initiator, "foundgine", tenant, tenant);
        });
        engine = new InProcessFoundgineWorkflowEngine(activities);
        starter = new FoundgineWorkflowStarter(engine, catalog, options);
    }

    @AfterEach
    void close() { if (engine != null) engine.close(); }

    private String begin(String key) {
        return starter.start(new FoundgineWorkflowStartRequest("t1", "alice", "order", key, "{}")).workflowId();
    }

    private FoundgineWorkflowStatus finish(String id) throws InterruptedException {
        return engine.awaitCompletion(id, WAIT);
    }

    private void awaitApproval(String id) throws InterruptedException {
        long deadline = System.nanoTime() + WAIT.toNanos();
        while (System.nanoTime() < deadline) {
            var d = engine.describe(id);
            if (d != null && d.process() != null && d.process().pendingApprovalStep() != null) return;
            Thread.sleep(5);
        }
        throw new AssertionError("workflow never reached an approval gate");
    }

    @Test
    void completesAllStepsAndPassesEarlierOutputsForward() throws Exception {
        operations.add(op("a", c -> "out-a"));
        operations.add(op("b", c -> "saw:" + c.outputs().get("a")));
        start(new FoundgineProcessStep("a", "a"), new FoundgineProcessStep("b", "b"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        var result = engine.result(id);
        assertEquals(FoundgineProcessState.COMPLETED, result.outcome());
        assertEquals("out-a", result.outputs().get("a"));
        assertEquals("saw:out-a", result.outputs().get("b"));
        assertEquals(List.of("a", "b"), log);
    }

    @Test
    void nonRetryableFailureCompensatesInReverseOrder() throws Exception {
        operations.add(op("a", c -> null));
        operations.add(op("b", c -> null));
        operations.add(op("undo-a", c -> null));
        operations.add(op("undo-b", c -> null));
        operations.add(failing("c", () -> new FoundgineNonRetryableOperationException("boom")));
        start(new FoundgineProcessStep("a", "a", "undo-a"), new FoundgineProcessStep("b", "b", "undo-b"),
                new FoundgineProcessStep("c", "c"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.FAILED, finish(id));
        assertEquals(List.of("a", "b", "c", "undo-b", "undo-a"), log); // c ran once: non-retryable
    }

    @Test
    void transientFailuresAreRetriedUntilSuccess() throws Exception {
        var calls = new AtomicInteger();
        operations.add(op("flaky", c -> {
            if (calls.incrementAndGet() < 3) throw new IllegalStateException("transient");
            return "ok";
        }));
        start(new FoundgineProcessStep("s", "flaky"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        assertEquals(3, calls.get());
        assertEquals("ok", engine.result(id).outputs().get("s"));
    }

    @Test
    void exhaustedRetriesFailTheProcess() throws Exception {
        var calls = new AtomicInteger();
        operations.add(op("down", c -> { calls.incrementAndGet(); throw new IllegalStateException("down"); }));
        start(new FoundgineProcessStep("s", "down"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.FAILED, finish(id));
        assertEquals(3, calls.get());
    }

    @Test
    void authorizationFailuresAreNotRetried() throws Exception {
        operations.add(failing("denied", () -> new SecurityException("denied")));
        start(new FoundgineProcessStep("s", "denied"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.FAILED, finish(id));
        assertEquals(List.of("denied"), log);
    }

    @Test
    void unregisteredOperationFailsWithoutRetry() throws Exception {
        start(new FoundgineProcessStep("s", "missing"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.FAILED, finish(id));
        assertTrue(log.isEmpty());
    }

    @Test
    void approvalGatePausesUntilApproved() throws Exception {
        operations.add(op("a", c -> null));
        operations.add(op("ship", c -> null));
        start(new FoundgineProcessStep("a", "a"), new FoundgineProcessStep("ship", "ship", null, true, null));
        var id = begin("k1");
        awaitApproval(id);
        assertEquals(FoundgineProcessState.AWAITING_APPROVAL, engine.describe(id).process().state());
        assertEquals(List.of("a"), log);
        assertTrue(engine.signalDecision(id, new FoundgineApprovalDecision("ship", true, null, "boss")));
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        assertEquals(List.of("a", "ship"), log);
    }

    @Test
    void rejectedApprovalCompensatesAndEndsRejected() throws Exception {
        operations.add(op("a", c -> null));
        operations.add(op("undo-a", c -> null));
        operations.add(op("ship", c -> null));
        start(new FoundgineProcessStep("a", "a", "undo-a"), new FoundgineProcessStep("ship", "ship", null, true, null));
        var id = begin("k1");
        awaitApproval(id);
        engine.signalDecision(id, new FoundgineApprovalDecision("ship", false, "no", "boss"));
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id)); // like Temporal: the workflow returns normally
        assertEquals(FoundgineProcessState.REJECTED, engine.result(id).outcome());
        assertEquals("no", engine.result(id).failure());
        assertEquals(List.of("a", "undo-a"), log);
    }

    @Test
    void firstDecisionPerStepWins() throws Exception {
        operations.add(op("ship", c -> null));
        start(new FoundgineProcessStep("ship", "ship", null, true, null));
        var id = begin("k1");
        awaitApproval(id);
        engine.signalDecision(id, new FoundgineApprovalDecision("ship", true, null, "boss"));
        engine.signalDecision(id, new FoundgineApprovalDecision("ship", false, "late", "other"));
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        assertEquals(FoundgineProcessState.COMPLETED, engine.result(id).outcome());
    }

    @Test
    void approvalTimeoutRejectsTheProcess() throws Exception {
        operations.add(op("ship", c -> null));
        start(new FoundgineProcessStep("ship", "ship", null, true, Duration.ofMillis(100)));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        assertEquals(FoundgineProcessState.REJECTED, engine.result(id).outcome());
        assertEquals("Approval timed out.", engine.result(id).failure());
        assertTrue(log.isEmpty());
    }

    @Test
    void cancelWhileWaitingStillRunsCompensation() throws Exception {
        operations.add(op("a", c -> null));
        operations.add(op("undo-a", c -> null));
        operations.add(op("ship", c -> null));
        start(new FoundgineProcessStep("a", "a", "undo-a"), new FoundgineProcessStep("ship", "ship", null, true, null));
        var id = begin("k1");
        awaitApproval(id);
        assertTrue(engine.cancel(id));
        assertEquals(FoundgineWorkflowStatus.CANCELED, finish(id));
        assertEquals(List.of("a", "undo-a"), log);
    }

    @Test
    void duplicateStartsAreAbsorbed() throws Exception {
        operations.add(op("a", c -> null));
        start(new FoundgineProcessStep("a", "a"));
        var first = starter.start(new FoundgineWorkflowStartRequest("t1", "alice", "order", "k1", "{}"));
        var second = starter.start(new FoundgineWorkflowStartRequest("t1", "alice", "order", "k1", "{}"));
        assertFalse(first.alreadyExisted());
        assertTrue(second.alreadyExisted());
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(first.workflowId()));
        var third = starter.start(new FoundgineWorkflowStartRequest("t1", "alice", "order", "k1", "{}"));
        assertTrue(third.alreadyExisted()); // finished successfully: rejected, not re-run
        assertEquals(List.of("a"), log);
    }

    @Test
    void failedWorkflowsMayBeStartedAgain() throws Exception {
        var calls = new AtomicInteger();
        operations.add(op("a", c -> {
            if (calls.incrementAndGet() == 1) throw new FoundgineNonRetryableOperationException("first");
            return "ok";
        }));
        start(new FoundgineProcessStep("a", "a"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.FAILED, finish(id));
        var again = starter.start(new FoundgineWorkflowStartRequest("t1", "alice", "order", "k1", "{}"));
        assertFalse(again.alreadyExisted());
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
    }

    @Test
    void workflowIdsAreBuiltFromValidatedSegments() {
        operations.add(op("a", c -> null));
        start(new FoundgineProcessStep("a", "a"));
        assertEquals("foundgine/t1/order/k-1", starter.workflowId("t1", "order", "k-1"));
        assertThrows(IllegalArgumentException.class, () -> starter.workflowId("t1/../t2", "order", "k"));
        assertThrows(IllegalArgumentException.class, () -> starter.workflowId("t1", "order", ""));
        assertThrows(IllegalArgumentException.class,
                () -> starter.start(new FoundgineWorkflowStartRequest("t1", "alice", "unknown", "k", "{}")));
    }

    @Test
    void eachFoundgineCallInsideAnOperationGetsAFreshContext() throws Exception {
        var seen = new ArrayList<SecurityExecutionContext>();
        operations.add(op("multi", c -> {
            seen.add(activities.securityProvider().getSecurityExecutionContext());
            seen.add(activities.securityProvider().getSecurityExecutionContext());
            return null;
        }));
        start(new FoundgineProcessStep("s", "multi"));
        var id = begin("k1");
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        assertEquals(2, seen.size());
        assertNotNull(seen.get(0));
        assertNotNull(seen.get(1));
        assertNotSame(seen.get(0).warrant(), seen.get(1).warrant());
        assertEquals("t1", seen.get(0).tenant());
        assertEquals("alice", seen.get(0).subject());
        assertEquals(2, factoryCalls.get());
    }

    @Test
    void outboxPublisherStartsRoutedTopicsAndAbsorbsRedelivery() throws Exception {
        operations.add(op("a", c -> null));
        start(new FoundgineProcessStep("a", "a"));
        options.topicRoutes.put("order.created", "order");
        var publisher = new FoundgineTemporalOutboxPublisher(starter, options);
        var message = new FoundgineOutboxMessage(UUID.randomUUID(), "t1", "order.created", "{}", null, null,
                java.util.Map.of("initiator", "svc-orders"), null);
        publisher.publish(message);
        publisher.publish(message); // at-least-once redelivery
        var id = starter.workflowId("t1", "order", message.id().toString().replace("-", ""));
        assertEquals(FoundgineWorkflowStatus.COMPLETED, finish(id));
        assertEquals(List.of("a"), log);
    }

    @Test
    void outboxPublisherFollowsTheUnroutedTopicPolicy() {
        operations.add(op("a", c -> null));
        start(new FoundgineProcessStep("a", "a"));
        var publisher = new FoundgineTemporalOutboxPublisher(starter, options);
        var message = new FoundgineOutboxMessage(UUID.randomUUID(), "t1", "other.topic", "{}");
        publisher.publish(message); // ignored by default
        options.unroutedTopicBehavior = UnroutedTopicBehavior.FAIL;
        assertThrows(IllegalStateException.class, () -> publisher.publish(message));
        assertTrue(log.isEmpty());
    }

    @Test
    void optionsRejectUnsafeValues() {
        var o = new FoundgineTemporalOptions();
        o.validate();
        o.workflowIdPrefix = "a/b";
        assertThrows(IllegalArgumentException.class, o::validate);
        o.workflowIdPrefix = "foundgine";
        o.activityMaximumAttempts = 0;
        assertThrows(IllegalArgumentException.class, o::validate);
    }
}
