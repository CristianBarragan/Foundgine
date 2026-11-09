package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.CancellationTokenSource;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.runtime.IFoundgineExecutor;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ExecutionException;
import org.junit.jupiter.api.Test;

/** Tests for increment 9e-1: missing context, authorization isolation, execution failures, cancellation, success. */
class FoundgineGraphQLQueryExecutorTest {
    private static final String QUERY = "query { customer { id name } }";

    private static SemanticModel model() {
        return new SemanticModelBuilder()
                .entity(new EntityId(1), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class))
                .build();
    }

    private static SecurityExecutionContext context(String subject, String tenant) {
        var now = Instant.now();
        var warrant = SecurityWarrant.ofDefaults(
                "warrant-1", "issuer", subject, "graphql",
                List.of(new CapabilityGrant("Customer.read", "read", List.of())),
                SecurityWarrantConstraints.UNRESTRICTED,
                now.minusSeconds(60), now.plusSeconds(600), "nonce-1", "issuer-key", null, null);
        return new SecurityExecutionContext(warrant, subject, "graphql", tenant, null);
    }

    private static SecurityExecutionContext context() {
        return context("subject-1", "tenant-a");
    }

    private static ISecurityExecutionContextProvider fixed(SecurityExecutionContext ctx) {
        return () -> ctx;
    }

    private static final class SequenceProvider implements ISecurityExecutionContextProvider {
        private final SecurityExecutionContext[] contexts;
        int reads;

        SequenceProvider(SecurityExecutionContext... contexts) {
            this.contexts = contexts;
        }

        @Override
        public SecurityExecutionContext getSecurityExecutionContext() {
            return contexts[reads++];
        }
    }

    private static final class RecordingExecutor implements IFoundgineExecutor {
        int calls;
        SemanticRequest received;
        CancellationToken receivedToken;
        ExecutionResult result = new ExecutionResult(List.of());
        RuntimeException failSynchronously;
        Throwable failAsynchronously;

        @Override
        public CompletionStage<ExecutionResult> executeAsync(
                SemanticRequest request, ExecutionContext context, CancellationToken token) {
            calls++;
            received = request;
            receivedToken = token;
            if (failSynchronously != null) throw failSynchronously;
            if (failAsynchronously != null) return CompletableFuture.failedFuture(failAsynchronously);
            return CompletableFuture.completedFuture(result);
        }

        @Override
        public CompletionStage<ExecutionResult> executeAsync(
                ReadIntent intent, ExecutionContext context, CancellationToken token) {
            throw new UnsupportedOperationException();
        }
    }

    private static FoundgineGraphQLQueryExecutor create(RecordingExecutor exec, ISecurityExecutionContextProvider p) {
        return new FoundgineGraphQLQueryExecutor(exec, new GraphQLSemanticAdapter(model()), p);
    }

    private static <T> T join(CompletionStage<T> stage) {
        try {
            return stage.toCompletableFuture().get();
        } catch (ExecutionException e) {
            if (e.getCause() instanceof RuntimeException re) throw re;
            throw new IllegalStateException(e.getCause());
        } catch (InterruptedException e) {
            throw new IllegalStateException(e);
        }
    }

    private static Throwable failureOf(CompletionStage<?> stage) {
        try {
            stage.toCompletableFuture().get();
            return null;
        } catch (CancellationException e) {
            return e; // CompletableFuture.get() rethrows cancellation directly rather than wrapping it
        } catch (ExecutionException e) {
            var c = e.getCause();
            return c instanceof CompletionException && c.getCause() != null ? c.getCause() : c;
        } catch (InterruptedException e) {
            throw new IllegalStateException(e);
        }
    }

    // ---- missing security context ----

    @Test
    void executeAsync_fails_and_never_executes_without_security_context() {
        var exec = new RecordingExecutor();
        var sut = create(exec, fixed(null));

        assertInstanceOf(SecurityException.class, failureOf(sut.executeAsync(QUERY)));
        assertEquals(0, exec.calls);
    }

    @Test
    void missing_context_is_reported_before_invalid_graphql_is_examined() {
        var exec = new RecordingExecutor();
        var result = join(create(exec, fixed(null)).tryExecuteAsync("query { nonExistentField"));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().category());
        assertEquals(0, exec.calls);
    }

    @Test
    void blank_subject_is_rejected() {
        var exec = new RecordingExecutor();
        var blank = new SecurityExecutionContext(context().warrant(), " ", "graphql", "t", null);
        var result = join(create(exec, fixed(blank)).tryExecuteAsync(QUERY));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertEquals(0, exec.calls);
    }

    @Test
    void provider_fault_fails_closed_without_leaking_internals() {
        var exec = new RecordingExecutor();
        ISecurityExecutionContextProvider throwing = () -> {
            throw new IllegalStateException("secret host internals");
        };
        var result = join(create(exec, throwing).tryExecuteAsync(QUERY));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertFalse(result.error().message().contains("secret host internals"));
        assertEquals(0, exec.calls);
    }

    // ---- authorization isolation ----

    @Test
    void each_call_uses_its_own_freshly_read_security_context() {
        var exec = new RecordingExecutor();
        var a = context("alice", "tenant-a");
        var b = context("bob", "tenant-b");
        var provider = new SequenceProvider(a, b);
        var sut = create(exec, provider);

        join(sut.executeAsync(QUERY));
        assertSame(a, exec.received.security());
        join(sut.executeAsync(QUERY));
        assertSame(b, exec.received.security());
        assertEquals(2, provider.reads);
    }

    @Test
    void failed_call_does_not_leak_context_into_the_next_call() {
        var exec = new RecordingExecutor();
        var sut = create(exec, new SequenceProvider(context("alice", "t"), null));

        join(sut.executeAsync(QUERY));
        var second = join(sut.tryExecuteAsync(QUERY));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, second.error().code());
        assertEquals(1, exec.calls);
    }

    @Test
    void host_context_replaces_any_security_carried_by_translation() {
        var exec = new RecordingExecutor();
        var host = context("trusted-host-subject", "trusted-tenant");
        join(create(exec, fixed(host)).executeAsync(QUERY));

        assertEquals("trusted-host-subject", exec.received.security().subject());
        assertEquals("trusted-tenant", exec.received.security().tenant());
    }

    // ---- execution failures ----

    @Test
    void authorization_denial_maps_to_forbidden() {
        var exec = new RecordingExecutor();
        exec.failAsynchronously = new SemanticAuthorizationException("denied: Customer.read");
        var result = join(create(exec, fixed(context())).tryExecuteAsync(QUERY));

        assertEquals(GraphQLAdapterErrorCode.FORBIDDEN, result.error().code());
        assertFalse(result.error().message().contains("Customer.read"));
    }

    @Test
    void provider_failure_maps_to_generic_execution_failed() {
        var exec = new RecordingExecutor();
        exec.failAsynchronously = new IllegalArgumentException("connection string xyz");
        var result = join(create(exec, fixed(context())).tryExecuteAsync(QUERY));

        assertEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
        assertFalse(result.error().message().contains("xyz"));
    }

    @Test
    void synchronous_executor_throw_is_also_structured() {
        var exec = new RecordingExecutor();
        exec.failSynchronously = new IllegalArgumentException("boom");
        var result = join(create(exec, fixed(context())).tryExecuteAsync(QUERY));

        assertEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
    }

    @Test
    void executeAsync_propagates_execution_exceptions() {
        var exec = new RecordingExecutor();
        exec.failAsynchronously = new IllegalArgumentException("boom");

        assertInstanceOf(IllegalArgumentException.class,
                failureOf(create(exec, fixed(context())).executeAsync(QUERY)));
    }

    @Test
    void bad_graphql_is_a_translation_error_and_does_not_execute() {
        var exec = new RecordingExecutor();
        var result = join(create(exec, fixed(context())).tryExecuteAsync("query { customer { doesNotExist } }"));

        assertFalse(result.isSuccess());
        assertNotEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertNotEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
        assertEquals(0, exec.calls);
    }

    // ---- cancellation ----

    @Test
    void cancellation_token_reaches_the_executor() {
        try (var cts = new CancellationTokenSource()) {
            var exec = new RecordingExecutor();
            join(create(exec, fixed(context())).executeAsync(QUERY, null, null, cts.token()));
            assertSame(cts.token(), exec.receivedToken);
        }
    }

    @Test
    void already_cancelled_token_fails_without_reading_context_or_executing() {
        try (var cts = new CancellationTokenSource()) {
            cts.cancel();
            var exec = new RecordingExecutor();
            var provider = new SequenceProvider(context());
            var stage = create(exec, provider).tryExecuteAsync(QUERY, null, null, cts.token());

            assertInstanceOf(CancellationException.class, failureOf(stage));
            assertEquals(0, exec.calls);
            assertEquals(0, provider.reads);
        }
    }

    @Test
    void cancellation_during_execution_is_not_converted_to_an_error() {
        var exec = new RecordingExecutor();
        exec.failAsynchronously = new CancellationException("cancelled");

        assertInstanceOf(CancellationException.class,
                failureOf(create(exec, fixed(context())).tryExecuteAsync(QUERY)));
    }

    // ---- success ----

    @Test
    void executeAsync_attaches_host_context_and_returns_result_with_shape() {
        var exec = new RecordingExecutor();
        var expected = new ExecutionResult(new ArrayList<>());
        exec.result = expected;
        var host = context("trusted", "trusted-tenant");

        var result = join(create(exec, fixed(host)).executeAsync(QUERY));

        assertSame(host, exec.received.security());
        assertSame(expected, result.execution());
        assertTrue(result.resultShape().fields().stream().anyMatch(f -> f.graphQLName().equals("id")));
        assertTrue(result.resultShape().fields().stream().anyMatch(f -> f.graphQLName().equals("name")));
    }

    @Test
    void tryExecuteAsync_succeeds_with_valid_context_and_query() {
        var result = join(create(new RecordingExecutor(), fixed(context())).tryExecuteAsync(QUERY));

        assertTrue(result.isSuccess());
        assertNotNull(result.data());
    }

    @Test
    void constructor_rejects_null_dependencies() {
        var adapter = new GraphQLSemanticAdapter(model());
        assertThrows(NullPointerException.class,
                () -> new FoundgineGraphQLQueryExecutor(null, adapter, fixed(null)));
        assertThrows(NullPointerException.class,
                () -> new FoundgineGraphQLQueryExecutor(new RecordingExecutor(), null, fixed(null)));
        assertThrows(NullPointerException.class,
                () -> new FoundgineGraphQLQueryExecutor(new RecordingExecutor(), adapter, null));
    }
}
