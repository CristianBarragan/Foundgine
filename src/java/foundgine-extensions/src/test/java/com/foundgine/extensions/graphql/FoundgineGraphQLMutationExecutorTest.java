package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.abstractions.MutationRelationshipSchema;
import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.CancellationTokenSource;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.execution.mutation.MutationResult;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.runtime.IFoundgineMutations;
import com.foundgine.runtime.MutationDryRunResult;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.runtime.MutationPlanApproval;
import com.foundgine.runtime.SemanticMutationRequest;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ExecutionException;
import org.junit.jupiter.api.Test;

/** Tests for increment 9e-2: missing context, authorization isolation, execution failures, cancellation, success. */
class FoundgineGraphQLMutationExecutorTest {
    private static final String MUTATION = "mutation { createCustomer(input: { name: \"Ada\" }) { id name } }";

    private static final ColumnId C1 = new ColumnId(1);
    private static final ColumnId C2 = new ColumnId(2);

    /** Customer(Id=C1 pk, Name=C2); matches {@link MutationAdapterFixtures#customer()}. */
    private static MutationSchema schema() {
        var customer = new MutationEntitySchema(
                MutationAdapterFixtures.CUSTOMER, "Customer", new LinkedHashSet<>(List.of(C1, C2)),
                fieldMap(), C1);
        return new MutationSchema() {
            @Override
            public MutationEntitySchema getEntity(EntityId id) {
                return customer;
            }

            @Override
            public MutationRelationshipSchema getRelationship(RelationshipId id) {
                throw new UnsupportedOperationException();
            }
        };
    }

    private static Map<FieldId, ColumnId> fieldMap() {
        var m = new LinkedHashMap<FieldId, ColumnId>();
        m.put(new FieldId(1), C1);
        m.put(new FieldId(2), C2);
        return m;
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


    private static final class RecordingMutations implements IFoundgineMutations {
        int calls;
        SemanticMutationRequest received;
        CancellationToken receivedToken;
        RuntimeException failSynchronously;
        Throwable failAsynchronously;
        MutationExecutionResult result = new MutationExecutionResult(
                new MutationBatchResult(List.of(new MutationResult(1, returned()))), "plan", "result", null, null);

        private static Map<FieldId, Object> returned() {
            var m = new LinkedHashMap<FieldId, Object>();
            m.put(new FieldId(1), 42L);
            m.put(new FieldId(2), "Ada");
            return m;
        }

        @Override
        public MutationDryRunResult dryRun(SemanticMutationRequest request) {
            throw new UnsupportedOperationException();
        }

        @Override
        public CompletionStage<MutationExecutionResult> executeAsync(
                SemanticMutationRequest request, ExecutionContext context, CancellationToken token) {
            calls++;
            received = request;
            receivedToken = token;
            if (failSynchronously != null) throw failSynchronously;
            if (failAsynchronously != null) return CompletableFuture.failedFuture(failAsynchronously);
            return CompletableFuture.completedFuture(result);
        }

        @Override
        public MutationPlanApproval approve(SemanticMutationRequest request, String approvedBy) {
            throw new UnsupportedOperationException();
        }

        @Override
        public CompletionStage<MutationExecutionResult> executeApprovedAsync(
                MutationPlanApproval approval, ExecutionContext context, CancellationToken token) {
            throw new UnsupportedOperationException();
        }
    }

    private static FoundgineGraphQLMutationExecutor create(RecordingMutations m, ISecurityExecutionContextProvider p) {
        return new FoundgineGraphQLMutationExecutor(m, MutationAdapterFixtures.customer().adapter(), schema(), p);
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
    void executeAsync_fails_and_never_writes_without_security_context() {
        var m = new RecordingMutations();

        assertInstanceOf(SecurityException.class, failureOf(create(m, fixed(null)).executeAsync(MUTATION)));
        assertEquals(0, m.calls);
    }

    @Test
    void missing_context_is_reported_before_invalid_graphql_is_examined() {
        var m = new RecordingMutations();
        var result = join(create(m, fixed(null)).tryExecuteAsync("mutation { nope"));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().category());
        assertEquals(0, m.calls);
    }

    @Test
    void blank_subject_is_rejected() {
        var m = new RecordingMutations();
        var blank = new SecurityExecutionContext(context().warrant(), " ", "graphql", "t", null);
        var result = join(create(m, fixed(blank)).tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertEquals(0, m.calls);
    }

    @Test
    void provider_fault_fails_closed_without_leaking_internals() {
        var m = new RecordingMutations();
        ISecurityExecutionContextProvider throwing = () -> {
            throw new IllegalStateException("secret host internals");
        };
        var result = join(create(m, throwing).tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertFalse(result.error().message().contains("secret host internals"));
        assertEquals(0, m.calls);
    }

    // ---- authorization isolation ----

    @Test
    void each_call_uses_its_own_freshly_read_security_context() {
        var m = new RecordingMutations();
        var a = context("alice", "tenant-a");
        var b = context("bob", "tenant-b");
        var provider = new SequenceProvider(a, b);
        var sut = create(m, provider);

        join(sut.executeAsync(MUTATION));
        assertSame(a, m.received.security());
        join(sut.executeAsync(MUTATION));
        assertSame(b, m.received.security());
        assertEquals(2, provider.reads);
    }

    @Test
    void failed_call_does_not_leak_context_into_the_next_call() {
        var m = new RecordingMutations();
        var sut = create(m, new SequenceProvider(context("alice", "t"), null));

        join(sut.executeAsync(MUTATION));
        var second = join(sut.tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, second.error().code());
        assertEquals(1, m.calls);
    }

    @Test
    void graphql_supplied_tenant_or_subject_is_never_forwarded_as_identity() {
        var m = new RecordingMutations();
        var sut = create(m, fixed(context("trusted-subject", "trusted-tenant")));

        var rejected = join(sut.tryExecuteAsync(
                "mutation { createCustomer(input: { name: \"x\", tenantId: \"evil\", subject: \"evil\" }) { id } }"));
        assertFalse(rejected.isSuccess());
        assertEquals(0, m.calls);

        join(sut.executeAsync(MUTATION));
        assertEquals("trusted-subject", m.received.security().subject());
        assertEquals("trusted-tenant", m.received.security().tenant());
    }

    // ---- execution failures ----

    @Test
    void authorization_denial_maps_to_forbidden() {
        var m = new RecordingMutations();
        m.failAsynchronously = new SemanticAuthorizationException("denied: Customer.write");
        var result = join(create(m, fixed(context())).tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.FORBIDDEN, result.error().code());
        assertFalse(result.error().message().contains("Customer.write"));
    }

    @Test
    void provider_failure_maps_to_generic_execution_failed() {
        var m = new RecordingMutations();
        m.failAsynchronously = new IllegalArgumentException("connection string xyz");
        var result = join(create(m, fixed(context())).tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
        assertFalse(result.error().message().contains("xyz"));
    }

    @Test
    void synchronous_engine_throw_is_also_structured() {
        var m = new RecordingMutations();
        m.failSynchronously = new IllegalArgumentException("boom");
        var result = join(create(m, fixed(context())).tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
    }

    @Test
    void executeAsync_propagates_execution_exceptions() {
        var m = new RecordingMutations();
        m.failAsynchronously = new IllegalArgumentException("boom");

        assertInstanceOf(IllegalArgumentException.class, failureOf(create(m, fixed(context())).executeAsync(MUTATION)));
    }

    @Test
    void bad_graphql_is_a_translation_error_and_never_writes() {
        var m = new RecordingMutations();
        var result = join(create(m, fixed(context()))
                .tryExecuteAsync("mutation { doesNotExist(input: { name: \"x\" }) { id } }"));

        assertFalse(result.isSuccess());
        assertNotEquals(GraphQLAdapterErrorCode.UNAUTHENTICATED, result.error().code());
        assertNotEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
        assertEquals(0, m.calls);
    }

    @Test
    void result_that_cannot_be_materialized_is_an_execution_failure() {
        var m = new RecordingMutations();
        m.result = new MutationExecutionResult(new MutationBatchResult(List.of()), "p", "r", null, null);
        var result = join(create(m, fixed(context())).tryExecuteAsync(MUTATION));

        assertEquals(GraphQLAdapterErrorCode.EXECUTION_FAILED, result.error().code());
    }

    // ---- cancellation ----

    @Test
    void cancellation_token_reaches_the_mutation_engine() {
        try (var cts = new CancellationTokenSource()) {
            var m = new RecordingMutations();
            join(create(m, fixed(context())).executeAsync(MUTATION, null, null, cts.token()));
            assertSame(cts.token(), m.receivedToken);
        }
    }

    @Test
    void already_cancelled_token_fails_without_reading_context_or_writing() {
        try (var cts = new CancellationTokenSource()) {
            cts.cancel();
            var m = new RecordingMutations();
            var provider = new SequenceProvider(context());
            var stage = create(m, provider).tryExecuteAsync(MUTATION, null, null, cts.token());

            assertInstanceOf(CancellationException.class, failureOf(stage));
            assertEquals(0, m.calls);
            assertEquals(0, provider.reads);
        }
    }

    @Test
    void cancellation_during_execution_is_not_converted_to_an_error() {
        var m = new RecordingMutations();
        m.failAsynchronously = new CancellationException("cancelled");

        assertInstanceOf(CancellationException.class, failureOf(create(m, fixed(context())).tryExecuteAsync(MUTATION)));
    }

    // ---- success ----

    @Test
    void executeAsync_attaches_host_context_and_returns_shaped_data() {
        var m = new RecordingMutations();
        var host = context("trusted", "trusted-tenant");

        var result = join(create(m, fixed(host)).executeAsync(MUTATION));

        assertSame(host, m.received.security());
        assertSame(m.result, result.execution());
        @SuppressWarnings("unchecked")
        var created = (Map<String, Object>) result.data().get("createCustomer");
        assertEquals(42L, created.get("id"));
        assertEquals("Ada", created.get("name"));
    }

    @Test
    void tryExecuteAsync_succeeds_with_valid_context_and_mutation() {
        var result = join(create(new RecordingMutations(), fixed(context())).tryExecuteAsync(MUTATION));

        assertTrue(result.isSuccess());
        assertNotNull(result.data());
    }

    @Test
    void constructor_rejects_null_dependencies() {
        var adapter = MutationAdapterFixtures.customer().adapter();
        var p = fixed(null);
        assertThrows(NullPointerException.class, () -> new FoundgineGraphQLMutationExecutor(null, adapter, schema(), p));
        assertThrows(NullPointerException.class,
                () -> new FoundgineGraphQLMutationExecutor(new RecordingMutations(), null, schema(), p));
        assertThrows(NullPointerException.class,
                () -> new FoundgineGraphQLMutationExecutor(new RecordingMutations(), adapter, null, p));
        assertThrows(NullPointerException.class,
                () -> new FoundgineGraphQLMutationExecutor(new RecordingMutations(), adapter, schema(), null));
    }
}