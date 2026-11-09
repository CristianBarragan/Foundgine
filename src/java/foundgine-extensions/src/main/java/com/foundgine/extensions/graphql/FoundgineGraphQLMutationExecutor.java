package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.MutationResultMaterializer;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContextProviderExtensions;
import com.foundgine.runtime.IFoundgineMutations;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.runtime.SemanticMutationRequest;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.function.Supplier;

/**
 * Secure GraphQL mutation executor (increment 9e-2), write-side counterpart of
 * {@link FoundgineGraphQLQueryExecutor}.
 *
 * <p>Pipeline, in order: cancellation check; host-provided security context is required and validated (fail closed)
 * before any GraphQL text is parsed, so an unauthenticated caller can never trigger translation, planning or a write;
 * batch translation through {@link GraphQLMutationAdapter}; conversion to a semantic operation graph; execution through
 * {@link IFoundgineMutations} with the host context and the caller's token; materialization and GraphQL shaping.
 *
 * <p>Tenant, subject, audience and warrant material come only from the host provider; GraphQL input has no path to them.
 * The context is read once per call and never cached.
 */
public final class FoundgineGraphQLMutationExecutor {
    private static final String TRANSPORT = "GraphQL";

    private final IFoundgineMutations mutations;
    private final GraphQLMutationAdapter adapter;
    private final MutationSchema schema;
    private final ISecurityExecutionContextProvider securityContextProvider;
    private final Supplier<ExecutionContext> contextFactory;

    public FoundgineGraphQLMutationExecutor(
            IFoundgineMutations mutations,
            GraphQLMutationAdapter adapter,
            MutationSchema schema,
            ISecurityExecutionContextProvider securityContextProvider) {
        this(mutations, adapter, schema, securityContextProvider, null);
    }

    public FoundgineGraphQLMutationExecutor(
            IFoundgineMutations mutations,
            GraphQLMutationAdapter adapter,
            MutationSchema schema,
            ISecurityExecutionContextProvider securityContextProvider,
            Supplier<ExecutionContext> contextFactory) {
        this.mutations = Objects.requireNonNull(mutations, "mutations");
        this.adapter = Objects.requireNonNull(adapter, "adapter");
        this.schema = Objects.requireNonNull(schema, "schema");
        this.securityContextProvider = Objects.requireNonNull(securityContextProvider, "securityContextProvider");
        this.contextFactory = contextFactory != null ? contextFactory : () -> ExecutionContext.EMPTY;
    }

    public CompletionStage<GraphQLMutationExecutionResult> executeAsync(String graphql) {
        return executeAsync(graphql, null, null, CancellationToken.NONE);
    }

    /**
     * Translates and executes a GraphQL mutation document. The returned stage fails with {@link SecurityException}
     * without a valid host security context, {@link CancellationException} on cancellation, and otherwise with the
     * translation/execution exception. Prefer {@link #tryExecuteAsync} for structured errors.
     */
    public CompletionStage<GraphQLMutationExecutionResult> executeAsync(
            String graphql, Map<String, Object> variables, String operationName, CancellationToken cancellationToken) {
        try {
            var token = cancellationToken == null ? CancellationToken.NONE : cancellationToken;
            token.throwIfCancellationRequested();
            var security = requireValidSecurityContext();
            var items = adapter.adaptBatchWithResultShape(graphql, variables, operationName);
            return executeItems(items, security, token);
        } catch (RuntimeException e) {
            return CompletableFuture.failedFuture(e);
        }
    }

    public CompletionStage<GraphQLAdapterResult<GraphQLMutationExecutionResult>> tryExecuteAsync(String graphql) {
        return tryExecuteAsync(graphql, null, null, CancellationToken.NONE);
    }

    /**
     * Same as {@link #executeAsync} but reports failures as structured {@link GraphQLAdapterError}s: missing/invalid
     * context -> UNAUTHENTICATED; translation failure -> BAD_USER_INPUT / GRAPHQL_VALIDATION_FAILED /
     * GRAPHQL_ADAPTER_ERROR; authorization denial -> FORBIDDEN; any other planning/execution/materialization failure ->
     * EXECUTION_FAILED with a generic message. Cancellation is never converted to an error.
     */
    public CompletionStage<GraphQLAdapterResult<GraphQLMutationExecutionResult>> tryExecuteAsync(
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
            var message = e instanceof SecurityException && e.getMessage() != null
                    ? e.getMessage()
                    : "GraphQL mutation execution requires a valid host-supplied SecurityExecutionContext.";
            return done(new GraphQLAdapterError(
                    GraphQLAdapterErrorCode.UNAUTHENTICATED, message, null, GraphQLAdapterErrorCode.UNAUTHENTICATED));
        }

        List<GraphQLMutationBatchItem> items;
        try {
            items = adapter.adaptBatchWithResultShape(graphql, variables, operationName);
        } catch (CancellationException e) {
            return CompletableFuture.failedFuture(e);
        } catch (RuntimeException e) {
            return done(GraphQLAdapterErrors.fromException(e));
        }

        CompletionStage<GraphQLMutationExecutionResult> stage;
        try {
            stage = executeItems(items, security, token);
        } catch (RuntimeException e) {
            stage = CompletableFuture.failedFuture(e);
        }

        return stage.handle((result, error) -> {
            if (error == null) return GraphQLAdapterResult.success(result);
            var cause = error instanceof CompletionException && error.getCause() != null ? error.getCause() : error;
            if (cause instanceof CancellationException ce) throw new CompletionException(ce);
            if (cause instanceof SemanticAuthorizationException || cause instanceof SecurityException)
                return GraphQLAdapterResult.<GraphQLMutationExecutionResult>failure(new GraphQLAdapterError(
                        GraphQLAdapterErrorCode.FORBIDDEN,
                        "The caller is not authorized to perform this mutation.",
                        null,
                        GraphQLAdapterErrorCode.FORBIDDEN));
            return GraphQLAdapterResult.<GraphQLMutationExecutionResult>failure(new GraphQLAdapterError(
                    GraphQLAdapterErrorCode.EXECUTION_FAILED,
                    "GraphQL mutation execution failed.",
                    null,
                    "INTERNAL_SERVER_ERROR"));
        });
    }

    private CompletionStage<GraphQLMutationExecutionResult> executeItems(
            List<GraphQLMutationBatchItem> items, SecurityExecutionContext security, CancellationToken token) {
        var intents = items.stream().map(x -> x.adaptation().intent()).toList();
        var graph = GraphQLMutationSemanticConverter.toSemanticGraph(intents, schema);
        var request = new SemanticMutationRequest(graph, security);

        return mutations.executeAsync(request, contextFactory.get(), token)
                .thenApply(execution -> materialize(items, execution));
    }

    private GraphQLMutationExecutionResult materialize(
            List<GraphQLMutationBatchItem> items, MutationExecutionResult execution) {
        var materializer = new MutationResultMaterializer(adapter.model());
        var materialized = materializer.materializeBatch(
                items.stream()
                        .map(x -> new MutationResultMaterializer.Item(x.resultKey(), x.adaptation().intent()))
                        .toList(),
                execution.result());

        var byKey = new HashMap<String, GraphQLMutationBatchItem>();
        for (var item : items) byKey.put(item.resultKey(), item);

        var data = new LinkedHashMap<String, Object>();
        for (var item : materialized) {
            var shape = byKey.get(item.key()).adaptation().resultShape();
            data.put(item.key(), GraphQLMutationResultShaper.shapeRoot(item.result(), shape));
        }
        return new GraphQLMutationExecutionResult(execution, data);
    }

    private SecurityExecutionContext requireValidSecurityContext() {
        var security = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                securityContextProvider, TRANSPORT, "mutation execution");
        if (security.subject().isBlank() || security.audience().isBlank())
            throw new SecurityException(
                    "GraphQL mutation execution requires a SecurityExecutionContext with a warrant, subject and audience.");
        return security;
    }

    private static CompletionStage<GraphQLAdapterResult<GraphQLMutationExecutionResult>> done(GraphQLAdapterError error) {
        return CompletableFuture.completedFuture(GraphQLAdapterResult.failure(error));
    }
}
