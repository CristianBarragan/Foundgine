package com.foundgine.extensions.graphql;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContextProviderExtensions;
import com.foundgine.runtime.IFoundgineExecutor;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.function.Supplier;

/**
 * Secure GraphQL query executor (increment 9e-1). Port of {@code FoundgineGraphQLQueryExecutor}.
 *
 * <p>Pipeline, in order: cancellation check; host-provided security context is required and validated (fail closed)
 * before any GraphQL text is parsed; translation through {@link GraphQLSemanticAdapter}; the host context replaces
 * whatever security the translation carried; execution through {@link IFoundgineExecutor} with the caller's token.
 *
 * <p>The context is read from the provider once per call and never cached. GraphQL variables and query text have no
 * path to identity, tenant, audience or warrant material.
 */
public final class FoundgineGraphQLQueryExecutor {
    private static final String TRANSPORT = "GraphQL";

    private final IFoundgineExecutor executor;
    private final GraphQLSemanticAdapter adapter;
    private final ISecurityExecutionContextProvider securityContextProvider;
    private final Supplier<ExecutionContext> contextFactory;

    public FoundgineGraphQLQueryExecutor(
            IFoundgineExecutor executor,
            GraphQLSemanticAdapter adapter,
            ISecurityExecutionContextProvider securityContextProvider) {
        this(executor, adapter, securityContextProvider, null);
    }

    public FoundgineGraphQLQueryExecutor(
            IFoundgineExecutor executor,
            GraphQLSemanticAdapter adapter,
            ISecurityExecutionContextProvider securityContextProvider,
            Supplier<ExecutionContext> contextFactory) {
        this.executor = Objects.requireNonNull(executor, "executor");
        this.adapter = Objects.requireNonNull(adapter, "adapter");
        this.securityContextProvider = Objects.requireNonNull(securityContextProvider, "securityContextProvider");
        this.contextFactory = contextFactory != null ? contextFactory : () -> ExecutionContext.EMPTY;
    }

    public CompletionStage<GraphQLQueryExecutionResult> executeAsync(String graphql) {
        return executeAsync(graphql, null, null, CancellationToken.NONE);
    }

    /**
     * Translates and executes a GraphQL query. The returned stage fails with {@link SecurityException} when no valid
     * security context is available, with {@link CancellationException} when the token is cancelled, and otherwise with
     * the translation or execution exception. Prefer {@link #tryExecuteAsync} for structured errors.
     */
    public CompletionStage<GraphQLQueryExecutionResult> executeAsync(
            String graphql, Map<String, Object> variables, String operationName, CancellationToken cancellationToken) {
        try {
            var token = cancellationToken == null ? CancellationToken.NONE : cancellationToken;
            token.throwIfCancellationRequested();
            var security = requireValidSecurityContext();
            var adaptation = adapter.adaptResultShape(graphql, variables, operationName);
            var request = withSecurity(adaptation.request(), security);
            return executor.executeAsync(request, contextFactory.get(), token)
                    .thenApply(execution -> new GraphQLQueryExecutionResult(execution, adaptation.result()));
        } catch (RuntimeException e) {
            return CompletableFuture.failedFuture(e);
        }
    }

    public CompletionStage<GraphQLAdapterResult<GraphQLQueryExecutionResult>> tryExecuteAsync(String graphql) {
        return tryExecuteAsync(graphql, null, null, CancellationToken.NONE);
    }

    /**
     * Same as {@link #executeAsync} but reports failures as a structured {@link GraphQLAdapterError}: missing/invalid
     * context -> UNAUTHENTICATED; translation failure -> BAD_USER_INPUT / GRAPHQL_VALIDATION_FAILED /
     * GRAPHQL_ADAPTER_ERROR; authorization denial during execution -> FORBIDDEN; any other execution failure ->
     * EXECUTION_FAILED with a generic message. Cancellation is never converted to an error: the stage fails with
     * {@link CancellationException}.
     */
    public CompletionStage<GraphQLAdapterResult<GraphQLQueryExecutionResult>> tryExecuteAsync(
            String graphql, Map<String, Object> variables, String operationName, CancellationToken cancellationToken) {
        var token = cancellationToken == null ? CancellationToken.NONE : cancellationToken;
        try {
            token.throwIfCancellationRequested();
        } catch (CancellationException e) {
            return CompletableFuture.failedFuture(e);
        }

        SecurityExecutionContext security;
        try {
            security = requireValidSecurityContext();
        } catch (RuntimeException e) {
            // Fail closed; provider faults are reported generically so host internals are not disclosed.
            var message = e instanceof SecurityException && e.getMessage() != null
                    ? e.getMessage()
                    : "GraphQL execution requires a valid host-supplied SecurityExecutionContext.";
            return done(new GraphQLAdapterError(
                    GraphQLAdapterErrorCode.UNAUTHENTICATED, message, null, GraphQLAdapterErrorCode.UNAUTHENTICATED));
        }

        GraphQLQueryAdaptation adaptation;
        SemanticRequest request;
        try {
            adaptation = adapter.adaptResultShape(graphql, variables, operationName);
            request = withSecurity(adaptation.request(), security);
        } catch (CancellationException e) {
            return CompletableFuture.failedFuture(e);
        } catch (RuntimeException e) {
            return done(GraphQLAdapterErrors.fromException(e));
        }

        CompletionStage<ExecutionResult> stage;
        try {
            stage = executor.executeAsync(request, contextFactory.get(), token);
        } catch (RuntimeException e) {
            stage = CompletableFuture.failedFuture(e);
        }

        final var shape = adaptation.result();
        return stage.handle((execution, error) -> {
            if (error == null)
                return GraphQLAdapterResult.success(new GraphQLQueryExecutionResult(execution, shape));
            var cause = error instanceof CompletionException && error.getCause() != null ? error.getCause() : error;
            if (cause instanceof CancellationException ce) throw new CompletionException(ce);
            if (cause instanceof SemanticAuthorizationException || cause instanceof SecurityException)
                return GraphQLAdapterResult.<GraphQLQueryExecutionResult>failure(new GraphQLAdapterError(
                        GraphQLAdapterErrorCode.FORBIDDEN,
                        "The caller is not authorized to perform this query.",
                        null,
                        GraphQLAdapterErrorCode.FORBIDDEN));
            return GraphQLAdapterResult.<GraphQLQueryExecutionResult>failure(new GraphQLAdapterError(
                    GraphQLAdapterErrorCode.EXECUTION_FAILED,
                    "GraphQL query execution failed.",
                    null,
                    "INTERNAL_SERVER_ERROR"));
        });
    }

    private SecurityExecutionContext requireValidSecurityContext() {
        var security = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                securityContextProvider, TRANSPORT, "execution");
        if (security.subject().isBlank() || security.audience().isBlank())
            throw new SecurityException(
                    "GraphQL execution requires a SecurityExecutionContext with a warrant, subject and audience.");
        return security;
    }

    private static SemanticRequest withSecurity(SemanticRequest request, SecurityExecutionContext security) {
        return new SemanticRequest(request.root(), request.selections(), request.options(), security);
    }

    private static CompletionStage<GraphQLAdapterResult<GraphQLQueryExecutionResult>> done(GraphQLAdapterError error) {
        return CompletableFuture.completedFuture(GraphQLAdapterResult.failure(error));
    }
}
