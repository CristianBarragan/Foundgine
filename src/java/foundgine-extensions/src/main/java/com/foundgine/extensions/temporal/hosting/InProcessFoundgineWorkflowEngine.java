package com.foundgine.extensions.temporal.hosting;

import com.foundgine.core.execution.CancellationTokenSource;
import com.foundgine.extensions.temporal.activities.FoundgineActivityFailureException;
import com.foundgine.extensions.temporal.activities.FoundgineActivityInfo;
import com.foundgine.extensions.temporal.activities.FoundgineOperationActivities;
import com.foundgine.extensions.temporal.contracts.FoundgineActivityPolicy;
import com.foundgine.extensions.temporal.contracts.FoundgineApprovalDecision;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationRequest;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationResult;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessInput;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessResult;
import com.foundgine.extensions.temporal.workflows.FoundgineProcessFailedException;
import com.foundgine.extensions.temporal.workflows.FoundgineProcessWorkflow;
import com.foundgine.extensions.temporal.workflows.FoundgineWorkflowCancelledException;
import com.foundgine.extensions.temporal.workflows.IFoundgineProcessWorkflowContext;
import java.time.Duration;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CancellationException;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.BooleanSupplier;

/**
 * In-process, non-durable reference engine for {@link FoundgineProcessWorkflow}: it runs each workflow on its own
 * thread and implements activity retry/timeout, signals, queries and cancellation with the same semantics the
 * Temporal worker provides. It is meant for samples, tests and single-node development. State is lost on restart,
 * so production hosts adapt the Temporal Java SDK to {@link IFoundgineWorkflowClient} instead (the workflow and
 * activity classes are unchanged).
 */
public final class InProcessFoundgineWorkflowEngine implements IFoundgineWorkflowClient, AutoCloseable {
    private final FoundgineOperationActivities activities;
    private final ExecutorService executor = Executors.newCachedThreadPool(r -> {
        var t = new Thread(r, "foundgine-workflow");
        t.setDaemon(true);
        return t;
    });
    private final Map<String, Run> runs = new ConcurrentHashMap<>();

    public InProcessFoundgineWorkflowEngine(FoundgineOperationActivities activities) {
        this.activities = Objects.requireNonNull(activities, "activities");
    }

    @Override
    public boolean start(String workflowId, FoundgineProcessInput input) {
        var created = new Run(workflowId, input);
        var holder = new boolean[] {false};
        runs.compute(workflowId, (id, existing) -> {
            if (existing != null && existing.status != FoundgineWorkflowStatus.FAILED
                    && existing.status != FoundgineWorkflowStatus.CANCELED) return existing; // running or completed
            holder[0] = true;
            return created;
        });
        if (holder[0]) executor.execute(created::execute);
        return holder[0];
    }

    @Override
    public FoundgineWorkflowDescription describe(String workflowId) {
        var run = runs.get(workflowId);
        if (run == null) return null;
        var status = run.status;
        return new FoundgineWorkflowDescription(workflowId, status,
                status == FoundgineWorkflowStatus.RUNNING ? run.workflow.getStatus() : null);
    }

    @Override
    public boolean signalDecision(String workflowId, FoundgineApprovalDecision decision) {
        var run = runs.get(workflowId);
        if (run == null) return false;
        run.workflow.decide(decision);
        run.wake();
        return true;
    }

    @Override
    public boolean cancel(String workflowId) {
        var run = runs.get(workflowId);
        if (run == null) return false;
        run.cancelled = true;
        run.wake();
        return true;
    }

    /** Waits for a workflow to leave the running state. @return the final status, or RUNNING on timeout, or null if unknown. */
    public FoundgineWorkflowStatus awaitCompletion(String workflowId, Duration timeout) throws InterruptedException {
        var run = runs.get(workflowId);
        if (run == null) return null;
        return run.await(timeout);
    }

    /** The result of a completed workflow, or null. */
    public FoundgineProcessResult result(String workflowId) {
        var run = runs.get(workflowId);
        return run == null ? null : run.result;
    }

    @Override public void close() { executor.shutdownNow(); }

    private final class Run implements IFoundgineProcessWorkflowContext {
        final String id;
        final FoundgineProcessInput input;
        final FoundgineProcessWorkflow workflow = new FoundgineProcessWorkflow();
        final Object gate = new Object();
        volatile FoundgineWorkflowStatus status = FoundgineWorkflowStatus.RUNNING;
        volatile boolean cancelled;
        volatile FoundgineProcessResult result;
        private int activitySequence;

        Run(String id, FoundgineProcessInput input) { this.id = id; this.input = input; }

        void execute() {
            FoundgineWorkflowStatus finalStatus;
            try {
                result = workflow.run(input, this);
                finalStatus = FoundgineWorkflowStatus.COMPLETED;
            } catch (FoundgineWorkflowCancelledException ex) {
                finalStatus = FoundgineWorkflowStatus.CANCELED;
            } catch (FoundgineProcessFailedException ex) {
                finalStatus = FoundgineWorkflowStatus.FAILED;
            } catch (RuntimeException ex) {
                finalStatus = FoundgineWorkflowStatus.FAILED;
            }
            synchronized (gate) { status = finalStatus; gate.notifyAll(); }
        }

        void wake() { synchronized (gate) { gate.notifyAll(); } }

        FoundgineWorkflowStatus await(Duration timeout) throws InterruptedException {
            long deadline = System.nanoTime() + timeout.toNanos();
            synchronized (gate) {
                while (status == FoundgineWorkflowStatus.RUNNING) {
                    long left = deadline - System.nanoTime();
                    if (left <= 0) break;
                    TimeUnit.NANOSECONDS.timedWait(gate, left);
                }
                return status;
            }
        }

        @Override
        public boolean waitCondition(BooleanSupplier condition, Duration timeout) {
            long deadline = timeout == null ? Long.MAX_VALUE : System.nanoTime() + timeout.toNanos();
            synchronized (gate) {
                while (true) {
                    if (condition.getAsBoolean()) return true;
                    if (cancelled) throw new FoundgineWorkflowCancelledException();
                    long left = timeout == null ? 0 : deadline - System.nanoTime();
                    if (timeout != null && left <= 0) return false;
                    try {
                        if (timeout == null) gate.wait(); else TimeUnit.NANOSECONDS.timedWait(gate, left);
                    } catch (InterruptedException e) {
                        Thread.currentThread().interrupt();
                        throw new FoundgineWorkflowCancelledException();
                    }
                }
            }
        }

        @Override
        public FoundgineOperationResult executeActivity(FoundgineOperationRequest request, FoundgineActivityPolicy policy,
                boolean cancellable) {
            var activityId = Integer.toString(++activitySequence); // stable across retries of this activity
            FoundgineActivityFailureException last = null;
            for (int attempt = 1; attempt <= policy.maximumAttempts(); attempt++) {
                if (cancellable && cancelled) throw new FoundgineWorkflowCancelledException();
                try {
                    return attempt(request, policy, cancellable, new FoundgineActivityInfo(id, activityId, attempt));
                } catch (FoundgineActivityFailureException ex) {
                    last = ex;
                    if (ex.nonRetryable()) throw ex;
                }
                if (attempt < policy.maximumAttempts()) backoff(policy, attempt, cancellable);
            }
            throw last;
        }

        private FoundgineOperationResult attempt(FoundgineOperationRequest request, FoundgineActivityPolicy policy,
                boolean cancellable, FoundgineActivityInfo info) {
            try (var source = new CancellationTokenSource()) {
                Future<FoundgineOperationResult> future =
                        executor.submit(() -> activities.execute(request, info, source.token()));
                long deadline = System.nanoTime() + policy.startToCloseTimeout().toNanos();
                while (true) {
                    if (cancellable && cancelled) {
                        source.cancel();
                        future.cancel(true);
                        throw new FoundgineWorkflowCancelledException();
                    }
                    long left = deadline - System.nanoTime();
                    if (left <= 0) {
                        source.cancel();
                        future.cancel(true);
                        throw new FoundgineActivityFailureException("Activity timed out.", null, "Timeout", false);
                    }
                    try {
                        return future.get(Math.min(left, TimeUnit.MILLISECONDS.toNanos(25)), TimeUnit.NANOSECONDS);
                    } catch (TimeoutException tick) {
                        // poll again: lets workflow cancellation and the start-to-close timeout take effect
                    } catch (ExecutionException ex) {
                        var cause = ex.getCause();
                        if (cause instanceof FoundgineActivityFailureException f) throw f;
                        if (cause instanceof CancellationException) throw new FoundgineWorkflowCancelledException();
                        throw new FoundgineActivityFailureException(
                                cause.getMessage() == null ? cause.getClass().getSimpleName() : cause.getMessage(),
                                cause, null, false);
                    } catch (InterruptedException e) {
                        Thread.currentThread().interrupt();
                        throw new FoundgineWorkflowCancelledException();
                    }
                }
            }
        }

        private void backoff(FoundgineActivityPolicy policy, int attempt, boolean cancellable) {
            long millis = policy.initialInterval().toMillis() * (1L << Math.min(attempt - 1, 20));
            millis = Math.min(millis, policy.maximumInterval().toMillis());
            long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(millis);
            synchronized (gate) {
                while (true) {
                    if (cancellable && cancelled) throw new FoundgineWorkflowCancelledException();
                    long left = deadline - System.nanoTime();
                    if (left <= 0) return;
                    try {
                        TimeUnit.NANOSECONDS.timedWait(gate, left);
                    } catch (InterruptedException e) {
                        Thread.currentThread().interrupt();
                        throw new FoundgineWorkflowCancelledException();
                    }
                }
            }
        }
    }
}
