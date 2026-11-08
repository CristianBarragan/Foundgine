package com.foundgine.extensions.temporal.workflows;

import com.foundgine.extensions.temporal.activities.FoundgineActivityFailureException;
import com.foundgine.extensions.temporal.contracts.FoundgineApprovalDecision;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationRequest;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessInput;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessResult;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessState;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessStatus;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessStep;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Generic saga-style process: ordered steps, optional human-approval gates, reverse-order compensation on failure or
 * cancellation. ERP/CRM processes (order, onboarding, invoicing, ...) are process definitions. Port of
 * {@code FoundgineProcessWorkflow}; the code is deterministic and talks only to its
 * {@link IFoundgineProcessWorkflowContext}. Signals and queries may arrive on other threads, so state is guarded.
 */
public final class FoundgineProcessWorkflow {
    private final Map<String, FoundgineApprovalDecision> decisions = new HashMap<>();
    private final List<String> completed = new ArrayList<>();
    private final List<String> compensated = new ArrayList<>();
    private String state = FoundgineProcessState.RUNNING;
    private String process = "";
    private String currentStep;
    private String pendingApproval;
    private String failure;

    /** Query {@code status}. */
    public synchronized FoundgineProcessStatus getStatus() {
        return new FoundgineProcessStatus(process, state, currentStep, completed, compensated, pendingApproval, failure);
    }

    /** Signal {@code decision}. The first decision per step wins; duplicate signals are ignored so retries are harmless. */
    public synchronized void decide(FoundgineApprovalDecision decision) {
        decisions.putIfAbsent(decision.step(), decision);
    }

    private synchronized boolean hasDecision(String step) { return decisions.containsKey(step); }

    private synchronized FoundgineApprovalDecision decision(String step) { return decisions.get(step); }

    private synchronized void setState(String newState) { state = newState; }

    private synchronized void setFailure(String message) { failure = message; }

    public FoundgineProcessResult run(FoundgineProcessInput input, IFoundgineProcessWorkflowContext ctx) {
        synchronized (this) { process = input.definition().name(); }
        var outputs = new LinkedHashMap<String, String>();
        var executed = new ArrayList<FoundgineProcessStep>();
        try {
            for (var step : input.definition().steps()) {
                synchronized (this) { currentStep = step.name(); }

                if (step.requiresApproval()) {
                    synchronized (this) {
                        state = FoundgineProcessState.AWAITING_APPROVAL;
                        pendingApproval = step.name();
                    }
                    boolean decided = ctx.waitCondition(() -> hasDecision(step.name()), step.approvalTimeout());
                    synchronized (this) { pendingApproval = null; }
                    var decision = decided ? decision(step.name()) : null;
                    if (!decided || !decision.approved()) {
                        var reason = !decided ? "Approval timed out."
                                : (decision.reason() != null ? decision.reason() : "Rejected.");
                        setFailure(reason);
                        compensate(input, executed, outputs, ctx);
                        setState(FoundgineProcessState.REJECTED);
                        return new FoundgineProcessResult(FoundgineProcessState.REJECTED, outputs, reason);
                    }
                    setState(FoundgineProcessState.RUNNING);
                }

                var result = ctx.executeActivity(request(input, step.operation(), outputs, false),
                        input.activityPolicy(), true);
                outputs.put(step.name(), result.outputJson());
                executed.add(step);
                synchronized (this) { completed.add(step.name()); }
            }
            synchronized (this) { currentStep = null; state = FoundgineProcessState.COMPLETED; }
            return new FoundgineProcessResult(FoundgineProcessState.COMPLETED, outputs);
        } catch (FoundgineWorkflowCancelledException ex) {
            setFailure("Cancelled.");
            compensate(input, executed, outputs, ctx);
            setState(FoundgineProcessState.CANCELLED);
            throw ex; // preserve the Cancelled status
        } catch (FoundgineActivityFailureException ex) {
            var message = ex.getMessage();
            setFailure(message);
            compensate(input, executed, outputs, ctx);
            setState(FoundgineProcessState.FAILED);
            throw new FoundgineProcessFailedException(message, ex);
        }
    }

    private void compensate(FoundgineProcessInput input, List<FoundgineProcessStep> executed,
            Map<String, String> outputs, IFoundgineProcessWorkflowContext ctx) {
        if (executed.isEmpty()) return;
        setState(FoundgineProcessState.COMPENSATING);
        for (int i = executed.size() - 1; i >= 0; i--) {
            var step = executed.get(i);
            if (step.compensationOperation() == null) continue;
            try {
                // not cancellable: compensation must still run when the workflow itself was cancelled
                ctx.executeActivity(request(input, step.compensationOperation(), outputs, true),
                        input.activityPolicy(), false);
                synchronized (this) { compensated.add(step.name()); }
            } catch (FoundgineActivityFailureException ignored) {
                // keep unwinding the remaining steps; the process still ends Failed/Cancelled with the cause recorded
            }
        }
    }

    private static FoundgineOperationRequest request(FoundgineProcessInput input, String operation,
            Map<String, String> outputs, boolean compensation) {
        return new FoundgineOperationRequest(operation, input.tenantId(), input.initiator(), input.correlationId(),
                input.payloadJson(), outputs, compensation);
    }
}
