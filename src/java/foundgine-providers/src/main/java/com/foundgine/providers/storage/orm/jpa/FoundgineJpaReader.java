package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.ir.SemanticOperation;
import com.foundgine.core.semantic.ir.SemanticQuery;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;

import java.util.List;
import java.util.Objects;
import java.util.function.Consumer;

/**
 * Authorized semantic reader for JPA: the Java counterpart of the C# {@code FoundgineEfReader}.
 * Every read is {@code SemanticOperation -> authorization (with evidence) -> plan -> ExecutionIR -> provider plan ->
 * JPA Criteria}. There is no path that executes a query without the caller-bound authorization policy.
 *
 * <p>Differences from C#: the caller is an explicit argument (C# binds the authorizer in DI), and results are
 * detached JPA rows, as C# uses no-tracking EF. The C# reader's operation, field-list, {@code SemanticQuery}
 * and lambda overloads all exist here as {@code read}/{@code readResult}.
 *
 * <p>Created through {@link FoundgineOrm#reader(EntityManager)} (caller owns the EntityManager) or
 * {@link FoundgineOrm#reader(EntityManagerFactory)} (one EntityManager per read, closed afterwards).
 * Not thread-safe when wrapping a single EntityManager.
 *
 * <p>UNVERIFIED: not compiled against the real Jakarta Persistence jars. See ORM-JPA-PARITY-PLAN.md Step 72.
 */
public final class FoundgineJpaReader<C> {
    /** Rows of a semantic read plus the fingerprint of the semantic plan that produced them (C# {@code FoundgineReadResult}). */
    public record ReadResult(List<ExecutionRow> rows, String fingerprint) {
        public ReadResult {
            rows = List.copyOf(rows);
        }
    }

    private final FoundgineOrm<C> orm;
    private final EntityManager em;
    private final EntityManagerFactory emf;

    FoundgineJpaReader(FoundgineOrm<C> orm, EntityManager em, EntityManagerFactory emf) {
        this.orm = Objects.requireNonNull(orm, "orm");
        this.em = em;
        this.emf = emf;
    }

    /** Starts a fluent query over this reader's metadata: {@code reader.read(caller, reader.from(e).whereEq(..))}. */
    public SemanticQuery from(EntityId entity) { return orm.from(entity); }

    /** Full pipeline for an already-built operation, with the plan fingerprint. */
    public ReadResult readResult(C caller, SemanticOperation operation) {
        var lowered = orm.lower(caller, operation);
        var context = orm.executionContext(caller);
        List<ExecutionRow> rows;
        if (em != null) rows = run(em, lowered.plan(), context);
        else try (EntityManager scoped = emf.createEntityManager()) { rows = run(scoped, lowered.plan(), context); }
        return new ReadResult(rows, lowered.fingerprint());
    }

    public List<ExecutionRow> read(C caller, SemanticOperation operation) {
        return readResult(caller, operation).rows();
    }

    /** Executes a fluent {@link SemanticQuery}. */
    public List<ExecutionRow> read(C caller, SemanticQuery query) {
        return read(caller, query.build());
    }

    public ReadResult readResult(C caller, SemanticQuery query) {
        return readResult(caller, query.build());
    }

    /** Lambda form, the Java twin of C# {@code reader.ReadAsync(entity, q => ...)}. */
    public List<ExecutionRow> read(C caller, String entity, Consumer<SemanticQuery> configure) {
        var query = orm.from(entity);
        configure.accept(query);
        return read(caller, query);
    }

    public ReadResult readResult(C caller, String entity, Consumer<SemanticQuery> configure) {
        var query = orm.from(entity);
        configure.accept(query);
        return readResult(caller, query);
    }

    /** Id-addressed lambda form (C# {@code ReadAsync(EntityId, Func<SemanticQuery,SemanticQuery>)}). */
    public List<ExecutionRow> read(C caller, EntityId entity, Consumer<SemanticQuery> configure) {
        var query = orm.from(entity);
        configure.accept(query);
        return read(caller, query);
    }

    /** Single-entity field list (C# {@code ReadAsync(entity, fields, filter, order)}, equality filters ANDed). */
    public List<ExecutionRow> read(C caller, EntityId entity, List<FieldId> fields, java.util.Map<FieldId, Object> equals) {
        return read(caller, entity, q -> {
            q.select(fields.toArray(FieldId[]::new));
            equals.forEach(q::whereEq);
        });
    }

    /** Convenience: select {@code fields} where {@code filterField = value}. */
    public List<ExecutionRow> read(C caller, String entity, List<String> fields, String filterField, Object value) {
        return read(caller, entity, q -> q.select(fields).whereEq(filterField, value));
    }

    private List<ExecutionRow> run(EntityManager manager, com.foundgine.core.execution.ProviderPlan plan,
            com.foundgine.core.execution.ExecutionContext context) {
        return JpaOrmProvider.execution(orm.registry(), manager).execute(plan, context, CancellationToken.NONE).rows();
    }
}
