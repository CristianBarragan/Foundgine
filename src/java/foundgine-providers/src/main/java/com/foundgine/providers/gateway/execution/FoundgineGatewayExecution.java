package com.foundgine.providers.gateway.execution;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContextProviderExtensions;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.IFoundgineGatewayEventPublisher;
import com.foundgine.providers.gateway.diagnostics.FoundgineGatewayDiagnostics;
import com.foundgine.runtime.IFoundgineExecutor;
import com.foundgine.runtime.IFoundgineMutations;
import com.foundgine.runtime.MutationDryRunResult;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.runtime.MutationPlanApproval;
import com.foundgine.runtime.SemanticMutationRequest;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;

/** Secure adapter from gateway requests to Foundgine's canonical runtime interfaces. */
public final class FoundgineGatewayExecution implements IFoundgineGatewayExecution {
    private final IFoundgineExecutor executor;
    private final IFoundgineMutations mutations;
    private final ISecurityExecutionContextProvider security;
    private final IFoundgineGatewayEventPublisher events;

    public FoundgineGatewayExecution(IFoundgineExecutor executor, IFoundgineMutations mutations,
            ISecurityExecutionContextProvider security, IFoundgineGatewayEventPublisher events) {
        this.executor = Objects.requireNonNull(executor, "executor");
        this.mutations = Objects.requireNonNull(mutations, "mutations");
        this.security = Objects.requireNonNull(security, "security");
        this.events = Objects.requireNonNull(events, "events");
    }

    @Override
    public CompletionStage<ExecutionResult> executeReadAsync(ReadIntent intent, ExecutionContext context,
            CancellationToken cancellationToken) {
        Objects.requireNonNull(intent, "intent");
        return guarded("semantic.read", "read execution", sec -> {
            // Security material supplied in the request is never trusted; replace it with the host context.
            var secured = new ReadIntent(intent.rootEntity(), intent.selections(), intent.filter(), intent.order(),
                    intent.limit(), intent.offset(), intent.after(), sec);
            return executor.executeAsync(secured, context, token(cancellationToken));
        });
    }

    @Override
    public MutationDryRunResult dryRunMutation(SemanticMutationOperationGraph graph) {
        Objects.requireNonNull(graph, "graph");
        return mutations.dryRun(new SemanticMutationRequest(graph, requireSecurity("mutation dry-run")));
    }

    @Override
    public MutationPlanApproval approveMutation(SemanticMutationOperationGraph graph) {
        Objects.requireNonNull(graph, "graph");
        var sec = requireSecurity("mutation approval");
        // The approver identity is taken from the host-established security subject, never the payload.
        return mutations.approve(new SemanticMutationRequest(graph, sec), sec.subject());
    }

    @Override
    public CompletionStage<MutationExecutionResult> executeMutationAsync(SemanticMutationOperationGraph graph,
            ExecutionContext context, CancellationToken cancellationToken) {
        Objects.requireNonNull(graph, "graph");
        return guarded("semantic.mutation", "mutation execution", sec ->
                mutations.executeAsync(new SemanticMutationRequest(graph, sec), context, token(cancellationToken)));
    }

    @Override
    public CompletionStage<MutationExecutionResult> executeApprovedMutationAsync(MutationPlanApproval approval,
            ExecutionContext context, CancellationToken cancellationToken) {
        Objects.requireNonNull(approval, "approval");
        return guarded("semantic.mutation.approved", "approved mutation execution", sec -> {
            // Rebind to the current authenticated caller. The engine re-plans, re-authorizes and verifies the
            // approved semantic plan fingerprint before provider execution.
            var secured = new MutationPlanApproval(new SemanticMutationRequest(approval.request().graph(), sec),
                    approval.approvalId(), approval.planFingerprint(), approval.approvedBy(), approval.approvedAt());
            return mutations.executeApprovedAsync(secured, context, token(cancellationToken));
        });
    }

    private static CancellationToken token(CancellationToken t) { return t == null ? CancellationToken.NONE : t; }

    private SecurityExecutionContext requireSecurity(String operation) {
        return SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(security, "Gateway", operation);
    }

    /** Required start event (fail-closed) -> operation -> best-effort outcome notification. */
    private <T> CompletionStage<T> guarded(String subject, String operation,
            java.util.function.Function<SecurityExecutionContext, CompletionStage<T>> call) {
        final SecurityExecutionContext sec;
        final String correlationId;
        try {
            sec = requireSecurity(operation);
            correlationId = UUID.randomUUID().toString().replace("-", "");
            events.publish(FoundgineGatewayEventType.EXECUTION_STARTED, subject, correlationId, null, Map.of());
        } catch (Throwable failure) {
            // Nothing started: no outcome event. Surfaces as a failed stage, like an async C# method.
            return CompletableFuture.failedFuture(failure);
        }
        CompletionStage<T> stage;
        try {
            stage = call.apply(sec);
        } catch (Throwable failure) {
            // The engine may throw synchronously (for example plan re-authorization); report it like an async failure.
            stage = CompletableFuture.failedFuture(failure);
        }
        var result = new CompletableFuture<T>();
        stage.whenComplete((value, error) -> {
            if (error == null) {
                notifyOutcome(FoundgineGatewayEventType.EXECUTION_COMPLETED, subject, correlationId);
                result.complete(value);
                return;
            }
            var cause = unwrap(error);
            notifyOutcome(cause instanceof SecurityException
                    ? FoundgineGatewayEventType.AUTHORIZATION_DENIED : FoundgineGatewayEventType.EXECUTION_FAILED,
                    subject, correlationId);
            result.completeExceptionally(cause);
        });
        return result;
    }

    private static Throwable unwrap(Throwable t) {
        while (t instanceof CompletionException && t.getCause() != null) t = t.getCause();
        return t;
    }

    // Completion notifications are not the durable audit record. A post-commit notification failure must not
    // make a committed operation look like a failed operation to its caller.
    private void notifyOutcome(FoundgineGatewayEventType type, String subject, String correlationId) {
        try {
            events.publish(type, subject, correlationId, null, Map.of());
        } catch (RuntimeException failure) {
            FoundgineGatewayDiagnostics.recordHookFailure("execution-notification", false);
        }
    }
}
