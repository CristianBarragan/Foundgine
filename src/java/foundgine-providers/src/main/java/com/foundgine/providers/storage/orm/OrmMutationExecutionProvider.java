package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.execution.mutation.IMutationBatchExecutionProvider;
import com.foundgine.core.execution.mutation.IMutationSecurityConformanceEvaluator;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.execution.mutation.MutationResult;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import com.foundgine.core.semantic.planning.mutation.MutationOperation;
import com.foundgine.core.semantic.planning.mutation.MutationValueReference;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.security.SecurityInvariantIds;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Create / update / delete / upsert through an {@link IOrmMutationSource}. A batch runs in ONE
 * transaction (or the caller's), every operation is validated before any write, and any failure
 * rolls the whole batch back. Each operation is flushed so generated keys can be referenced by
 * later operations through {@code ReturnFields}.
 *
 * <p>Authorization is whatever the planner put in {@link MutationOperation#filter()}; this
 * provider adds no authorization of its own. Update/delete without a filter are refused.
 */
public final class OrmMutationExecutionProvider
        implements IMutationBatchExecutionProvider, IMutationSecurityConformanceEvaluator {
    private final String provider;
    private final IOrmMutationSource source;
    private final OrmRegistry registry;

    public OrmMutationExecutionProvider(
            String provider, IOrmMutationSource source, OrmRegistry registry) {
        this.provider = Objects.requireNonNull(provider, "provider");
        this.source = Objects.requireNonNull(source, "source");
        this.registry = Objects.requireNonNull(registry, "registry");
    }

    @Override
    public com.foundgine.core.execution.mutation.MutationSecurityConformanceResult evaluate(
            ExecutionMutationIR ir) {
        Objects.requireNonNull(ir, "ir");
        var violations = new ArrayList<String>();
        try {
            prepare(ir);
        } catch (RuntimeException e) {
            violations.add(e.getMessage());
        }
        return new com.foundgine.core.execution.mutation.MutationSecurityConformanceResult(
                getClass().getName(),
                List.of(SecurityInvariantIds.PARAMETERIZED_VALUES, SecurityInvariantIds.ATOMIC_MUTATION),
                violations);
    }

    @Override
    public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context) {
        return executeBatch(ir, context, CancellationToken.NONE);
    }

    @Override
    public MutationBatchResult executeBatch(
            ExecutionMutationIR ir, ExecutionContext context, CancellationToken token) {
        Objects.requireNonNull(ir, "ir");
        Objects.requireNonNull(context, "context");
        var prepared = prepare(ir); // validates every operation before any database work
        var results = new ArrayList<MutationResult>(ir.operations().size());

        try (var tx = source.begin()) {
            try {
                for (int i = 0; i < ir.operations().size(); i++) {
                    token.throwIfCancellationRequested();
                    context.ensureWithinDeadline();
                    var op = ir.operations().get(i);
                    var p = prepared.get(i);
                    var values = new LinkedHashMap<FieldId, Object>();
                    for (int f = 0; f < op.fields().size(); f++) {
                        var fv = op.fields().get(f);
                        Object raw =
                                fv.source() == null ? fv.value() : resolve(fv.source(), results, i);
                        var binding = p.map.field(p.fields.get(f));
                        values.put(p.fields.get(f), OrmValues.coerce(raw, binding.type()));
                    }
                    results.add(apply(op, p, values));
                }
                tx.commit();
            } catch (RuntimeException e) {
                tx.rollback();
                throw e;
            }
        }
        return new MutationBatchResult(results);
    }

    // ------------------------------------------------------------------ validation

    private record Prepared(OrmEntityMapping map, List<FieldId> fields, List<FieldId> conflict) {}

    private List<Prepared> prepare(ExecutionMutationIR ir) {
        ir.validateDerivedDependencies();
        var prepared = new ArrayList<Prepared>(ir.operations().size());
        for (var op : ir.operations()) {
            var map = registry.map(op.entity().id());
            var fields = new ArrayList<FieldId>(op.fields().size());
            for (var f : op.fields()) fields.add(fieldFor(map, f.column(), "Mutation column"));
            List<FieldId> conflict = null;
            if (op.conflictColumns() != null) {
                conflict = new ArrayList<>();
                for (var c : op.conflictColumns()) conflict.add(fieldFor(map, c, "Conflict column"));
            }
            validate(map, op, fields, conflict);
            prepared.add(new Prepared(map, fields, conflict));
        }
        return prepared;
    }

    private static FieldId fieldFor(
            OrmEntityMapping map, com.foundgine.core.abstractions.ColumnId column, String what) {
        return map.fieldForColumn(column)
                .orElseThrow(
                        () ->
                                new IllegalStateException(
                                        what
                                                + " '"
                                                + column.value()
                                                + "' is not a mapped field of '"
                                                + map.name()
                                                + "'."));
    }

    private static void validate(
            OrmEntityMapping map, MutationOperation op, List<FieldId> fields, List<FieldId> conflict) {
        MutationKind kind = op.kind();
        boolean writes = kind != MutationKind.DELETE;
        if ((kind == MutationKind.CREATE || kind == MutationKind.UPSERT)
                && !OrmEntityAccess.canCreate(map.type())) {
            throw new UnsupportedOperationException(
                    "ORM provider: entity '" + map.name() + "' has no no-arg constructor for creation.");
        }
        if (writes && fields.isEmpty()) {
            throw new IllegalStateException("Mutation of '" + map.name() + "' sets no fields.");
        }
        for (FieldId f : fields) {
            var b = map.field(f);
            if (!OrmEntityAccess.canSet(map.type(), b.name())) {
                throw new UnsupportedOperationException(
                        "ORM provider: field '" + map.name() + "." + b.name()
                                + "' is read-only and cannot be mutated.");
            }
        }
        if (op.returnFields() != null) for (FieldId f : op.returnFields()) map.field(f);

        SemanticFilterExpression filter = op.filter();
        if ((kind == MutationKind.UPDATE || kind == MutationKind.DELETE) && filter == null) {
            throw new IllegalStateException(
                    kind + " of '" + map.name()
                            + "' has no filter; the ORM provider refuses unfiltered updates and deletes.");
        }
        if (kind == MutationKind.CREATE && filter != null) {
            throw new UnsupportedOperationException(
                    "ORM provider: a create operation cannot carry a filter.");
        }
        if (filter != null) OrmQueries.lowerFilter(map, filter);

        if (kind == MutationKind.UPSERT) {
            if (conflict == null || conflict.isEmpty()) {
                throw new IllegalStateException(
                        "Upsert of '" + map.name() + "' declares no conflict columns.");
            }
            for (FieldId c : conflict) {
                var b = map.field(c);
                if (!fields.contains(c)) {
                    throw new IllegalStateException(
                            "Upsert conflict field '" + map.name() + "." + b.name()
                                    + "' is not among the values written.");
                }
            }
        }
    }

    // ------------------------------------------------------------------ execution

    private MutationResult apply(MutationOperation op, Prepared p, Map<FieldId, Object> values) {
        var map = p.map;
        switch (op.kind()) {
            case CREATE:
                return createOne(map, values, op.returnFields());
            case UPDATE:
                {
                    var matched = source.find(map, OrmQueries.lowerFilter(map, op.filter()));
                    for (Object e : matched) setValues(map, e, values);
                    if (!matched.isEmpty()) source.flush();
                    return new MutationResult(
                            matched.size(), returned(map, first(matched), op.returnFields()));
                }
            case DELETE:
                {
                    var matched = source.find(map, OrmQueries.lowerFilter(map, op.filter()));
                    var ret = returned(map, first(matched), op.returnFields()); // read before it is gone
                    for (Object e : matched) source.remove(map, e);
                    if (!matched.isEmpty()) source.flush();
                    return new MutationResult(matched.size(), ret);
                }
            case UPSERT:
                {
                    OrmPredicate match = new OrmPredicate.Const(true);
                    for (FieldId c : p.conflict) {
                        Object v = values.get(c);
                        if (v == null) {
                            throw new IllegalStateException(
                                    "Upsert conflict field '" + map.field(c).name()
                                            + "' is null; a null key cannot identify an existing row.");
                        }
                        match = OrmPredicate.and(match, new OrmPredicate.Compare(c, OrmPredicate.Op.EQ, v));
                    }
                    if (op.filter() != null) {
                        match = OrmPredicate.and(match, OrmQueries.lowerFilter(map, op.filter()));
                    }
                    var existing = source.find(map, match);
                    if (existing.isEmpty()) return createOne(map, values, op.returnFields());
                    if (existing.size() > 1) {
                        throw new IllegalStateException(
                                "Upsert of '" + map.name() + "' matched " + existing.size()
                                        + " rows; the conflict columns are not unique.");
                    }
                    setValues(map, existing.get(0), values);
                    source.flush();
                    return new MutationResult(1, returned(map, existing.get(0), op.returnFields()));
                }
            default:
                throw new UnsupportedOperationException(
                        "ORM provider: mutation kind '" + op.kind() + "' is not supported.");
        }
    }

    private MutationResult createOne(
            OrmEntityMapping map, Map<FieldId, Object> values, List<FieldId> returnFields) {
        Object entity = OrmEntityAccess.newInstance(map.type());
        setValues(map, entity, values);
        source.persist(map, entity);
        source.flush(); // database-generated keys are populated here, for later operations
        return new MutationResult(1, returned(map, entity, returnFields));
    }

    private static void setValues(OrmEntityMapping map, Object entity, Map<FieldId, Object> values) {
        for (var e : values.entrySet()) {
            OrmEntityAccess.set(entity, map.field(e.getKey()).name(), e.getValue());
        }
    }

    private static Map<FieldId, Object> returned(
            OrmEntityMapping map, Object entity, List<FieldId> fields) {
        if (fields == null || fields.isEmpty() || entity == null) return null;
        var result = new LinkedHashMap<FieldId, Object>();
        for (FieldId f : fields) result.put(f, OrmEntityAccess.get(entity, map.field(f).name()));
        return result;
    }

    private static Object first(List<Object> list) {
        return list.isEmpty() ? null : list.get(0);
    }

    private static Object resolve(
            MutationValueReference source, List<MutationResult> results, int current) {
        int idx = source.sourceOperationIndex();
        if (idx < 0 || idx >= current) {
            throw new IllegalStateException(
                    "Mutation value reference points to operation " + idx + " which has not run.");
        }
        var returned = results.get(idx).returnedValues();
        if (returned == null || !returned.containsKey(source.sourceField())) {
            throw new IllegalStateException(
                    "Operation " + idx + " did not return field " + source.sourceField().value()
                            + "; add it to ReturnFields.");
        }
        return returned.get(source.sourceField());
    }
}
