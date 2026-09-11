package com.foundgine.providers.storage.inmemory;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.AuthorizationPredicateKind;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionCellKey;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ExecutionIRBoundary;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.execution.IProviderPlanCompiler;
import com.foundgine.core.execution.ISecurityInvariantProviderCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.IMetadataProvider;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.core.semantic.security.SecurityInvariantIds;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.function.Function;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * Compiles ExecutionIR into an InMemoryPlan and executes that IR directly.
 *
 * <p>This intentionally mirrors the C# provider: metadata is used only for
 * relationship key resolution and authorization resource-member resolution;
 * backing-store fields never become visible unless the execution node selected
 * them.</p>
 */
public final class InMemoryCompiler implements IProviderPlanCompiler,
        ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {

    private final IMetadataProvider metadata;
    private final InMemoryDataSet data;

    /** Compile-only constructor, matching the C# provider's compatibility surface. */
    public InMemoryCompiler() {
        this.metadata = null;
        this.data = null;
    }

    public InMemoryCompiler(IMetadataProvider metadata, InMemoryDataSet data) {
        this.metadata = Objects.requireNonNull(metadata, "metadata");
        this.data = Objects.requireNonNull(data, "data");
    }

    @Override
    public Collection<String> preservedSecurityInvariants() {
        return List.of(
                SecurityInvariantIds.AUTHORIZATION_REQUIRED,
                SecurityInvariantIds.RUNTIME_AUTHORIZATION,
                SecurityInvariantIds.FIELD_VISIBILITY,
                SecurityInvariantIds.RELATIONSHIP_VISIBILITY,
                SecurityInvariantIds.PARAMETERIZED_VALUES,
                SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION);
    }

    @Override
    public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
        Objects.requireNonNull(ir, "ir");
        Objects.requireNonNull(plan, "plan");
        if (!(plan instanceof InMemoryPlan memoryPlan)) {
            throw new IllegalArgumentException("Expected an InMemoryPlan.");
        }

        var required = ir.requiredSecurityInvariants().stream().distinct().sorted().toList();
        var preserved = preservedSecurityInvariants();
        var satisfied = required.stream().filter(preserved::contains).toList();
        var violations = new ArrayList<String>();
        required.stream()
                .filter(x -> !satisfied.contains(x))
                .forEach(x -> violations.add(
                        "Invariant '" + x + "' is not certified by the in-memory execution plan."));

        if (memoryPlan.ir() != ir) {
            violations.add("The certified InMemoryPlan does not reference the supplied ExecutionIR.");
        }

        return new ProviderSecurityConformanceResult(
                memoryPlan.provider(), required, satisfied, List.copyOf(violations));
    }

    /** Compatibility bridge for callers holding a semantic plan. */
    public ProviderPlan compile(SemanticPlan plan) {
        return compile(ExecutionIRCompiler.compile(plan));
    }

    @Override
    public ProviderPlan compile(ExecutionIR ir) {
        Objects.requireNonNull(ir, "ir");
        var plan = new InMemoryPlan(ir);
        ExecutionIRBoundary.bindProviderPlan(ir, plan);
        return plan;
    }

    public ExecutionResult executeAsyncResult(
            ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken) {
        if (!(plan instanceof InMemoryPlan memoryPlan)) {
            throw new IllegalArgumentException("Expected an InMemoryPlan.");
        }
        if (memoryPlan.authorizationBinding() == null) {
            throw new IllegalStateException(
                    "The in-memory provider refuses to execute a provider plan without authorization provenance.");
        }
        cancellationToken.throwIfCancellationRequested();
        if (metadata == null || data == null) {
            throw new IllegalStateException(
                    "InMemoryCompiler execution requires metadata and data. Construct it with InMemoryCompiler(metadata, data).");
        }
        var rows = executeNode(memoryPlan.ir().root(), context, cancellationToken, null, true).toList();
        return new ExecutionResult(rows);
    }

    public java.util.concurrent.CompletionStage<ExecutionResult> executeAsync(
            ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken) {
        return java.util.concurrent.CompletableFuture.completedFuture(
                executeAsyncResult(plan, context, cancellationToken));
    }

    private Stream<ExecutionRow> executeNode(
            ExecutionIRNode node,
            ExecutionContext context,
            CancellationToken cancellationToken,
            InMemoryRow parent,
            boolean isRoot) {
        cancellationToken.throwIfCancellationRequested();

        Stream<InMemoryRow> candidates = parent == null
                ? data.get(node.entityId()).stream()
                : traverse(parent, node).stream();

        if (node.authorization() != null) {
            candidates = candidates.filter(row -> evaluateAuthorization(node.authorization(), row, context));
        }

        if (isRoot) {
            candidates = applyQueryOptions(candidates, node.queryOptions());
        }

        var rows = candidates.toList();
        var output = new ArrayList<ExecutionRow>();
        for (var row : rows) {
            cancellationToken.throwIfCancellationRequested();
            if (node.children().isEmpty()) {
                output.add(toExecutionRow(node, row));
                continue;
            }

            var childGroups = new ArrayList<List<ExecutionRow>>();
            for (var child : node.children()) {
                childGroups.add(executeNode(child, context, cancellationToken, row, false).toList());
            }

            for (var combination : cartesian(childGroups)) {
                var merged = new ArrayList<ExecutionRow>(1 + combination.size());
                merged.add(toExecutionRow(node, row));
                merged.addAll(combination);
                output.add(mergeRows(merged));
            }
        }
        return output.stream();
    }

    private List<InMemoryRow> traverse(InMemoryRow parent, ExecutionIRNode child) {
        RelationshipId relationshipId = child.viaRelationship();
        if (relationshipId == null) {
            throw new UnsupportedOperationException(
                    "The in-memory provider currently supports relationship traversal only.");
        }

        RelationshipMetadata relationship = metadata.getRelationship(relationshipId);
        FieldId sourceField = fieldForColumn(relationship.source(), relationship.sourceKey().columnId());
        FieldId targetField = fieldForColumn(relationship.target(), relationship.targetKey().columnId());
        Object parentValue = parent.values().get(sourceField);

        return data.get(child.entityId()).stream()
                .filter(row -> row.values().containsKey(targetField)
                        && Objects.equals(parentValue, row.values().get(targetField)))
                .toList();
    }

    private Stream<InMemoryRow> applyQueryOptions(Stream<InMemoryRow> rows, SemanticQueryOptions options) {
        if (options == null) return rows;

        if (options.filter() != null) {
            rows = rows.filter(row -> evaluateFilter(options.filter(), row));
        }

        Comparator<InMemoryRow> comparator = null;
        for (var term : options.effectiveOrder().stream().filter(x -> x.isRootField()).toList()) {
            Comparator<InMemoryRow> termComparator = Comparator.comparing(
                    row -> row.values().get(term.field()), this::compareValues);
            if (term.direction() == SemanticSortDirection.DESC) termComparator = termComparator.reversed();
            comparator = comparator == null ? termComparator : comparator.thenComparing(termComparator);
        }
        if (comparator != null) rows = rows.sorted(comparator);

        if (options.offset() != null) rows = rows.skip(options.offset());
        if (options.limit() != null) rows = rows.limit(options.limit());
        return rows;
    }

    private boolean evaluateFilter(SemanticFilterExpression filter, InMemoryRow row) {
        if (filter instanceof SemanticFieldFilter fieldFilter) {
            return compareValue(row.values().get(fieldFilter.field()),
                    fieldFilter.operator(), fieldFilter.value());
        }
        if (filter instanceof SemanticAndFilter and) {
            return and.expressions().stream().allMatch(x -> evaluateFilter(x, row));
        }
        if (filter instanceof SemanticOrFilter or) {
            return or.expressions().stream().anyMatch(x -> evaluateFilter(x, row));
        }
        throw new UnsupportedOperationException(
                "In-memory filter '" + filter.getClass().getSimpleName() + "' is not implemented.");
    }

    private boolean compareValue(Object actual, SemanticFilterOperator operator, Object expected) {
        return switch (operator) {
            case EQ -> Objects.equals(actual, expected);
            case NEQ -> !Objects.equals(actual, expected);
            case IN -> {
                if (expected instanceof Collection<?> collection) {
                    yield collection.stream().anyMatch(x -> Objects.equals(actual, x));
                }
                if (expected != null && expected.getClass().isArray()) {
                    int length = java.lang.reflect.Array.getLength(expected);
                    boolean found = false;
                    for (int i = 0; i < length; i++) {
                        if (Objects.equals(actual, java.lang.reflect.Array.get(expected, i))) {
                            found = true;
                            break;
                        }
                    }
                    yield found;
                }
                yield false;
            }
        };
    }

    private boolean evaluateAuthorization(
            AuthorizationPredicate predicate, InMemoryRow row, ExecutionContext context) {
        return switch (predicate.kind()) {
            case EQUAL -> Objects.equals(
                    evaluateValue(predicate.left(), row, context),
                    evaluateValue(predicate.right(), row, context));
            case NOT_EQUAL -> !Objects.equals(
                    evaluateValue(predicate.left(), row, context),
                    evaluateValue(predicate.right(), row, context));
            case AND -> evaluateAuthorization(predicate.left(), row, context)
                    && evaluateAuthorization(predicate.right(), row, context);
            case OR -> evaluateAuthorization(predicate.left(), row, context)
                    || evaluateAuthorization(predicate.right(), row, context);
            case NOT -> !evaluateAuthorization(predicate.left(), row, context);
            default -> toBoolean(evaluateValue(predicate, row, context));
        };
    }

    private Object evaluateValue(
            AuthorizationPredicate node, InMemoryRow row, ExecutionContext context) {
        return switch (node.kind()) {
            case CONSTANT -> parseConstant(node.value());
            case RESOURCE_PARAMETER -> row;
            case CONTEXT_PARAMETER -> context.tryGetValue(node.name()).orElse(null);
            case MEMBER_ACCESS -> readMember(node, row, context);
            case EQUAL, NOT_EQUAL, AND, OR, NOT -> evaluateAuthorization(node, row, context);
            default -> throw new UnsupportedOperationException(
                    "Authorization node '" + node.kind() + "' is not implemented.");
        };
    }

    private Object readMember(
            AuthorizationPredicate node, InMemoryRow row, ExecutionContext context) {
        var target = Objects.requireNonNull(node.left(), "Member access has no target.");
        if (target.kind() == AuthorizationPredicateKind.RESOURCE_PARAMETER) {
            var name = Objects.requireNonNull(node.name(), "Resource member has no name.");
            EntityMetadata entity = metadata.getEntity(row.entityId());
            FieldMetadata field = entity.effectiveFields().stream()
                    .filter(x -> x.name().equals(name))
                    .findFirst()
                    .orElseThrow(() -> new IllegalStateException(
                            "Authorization resource member '" + entity.name() + "." + name
                                    + "' has no field mapping."));
            return row.values().get(field.id());
        }
        if (target.kind() == AuthorizationPredicateKind.CONTEXT_PARAMETER) {
            String path = (target.name() == null ? "" : target.name())
                    + "." + (node.name() == null ? "" : node.name());
            return context.tryGetValue(path).orElse(null);
        }
        throw new UnsupportedOperationException(
                "Only context and resource member authorization is supported by this minimal provider.");
    }

    private ExecutionRow toExecutionRow(ExecutionIRNode node, InMemoryRow row) {
        var values = new LinkedHashMap<String, Object>();
        var cells = new LinkedHashMap<ExecutionCellKey, Object>();
        for (FieldId field : node.fields()) {
            Object value = row.values().get(field);
            values.put("__fg_" + node.id() + "_" + field, value);
            cells.put(new ExecutionCellKey(node.id(), node.entityId(), field), value);
        }
        return new ExecutionRow(values, cells);
    }

    private static ExecutionRow mergeRows(List<ExecutionRow> rows) {
        var values = new LinkedHashMap<String, Object>();
        var cells = new LinkedHashMap<ExecutionCellKey, Object>();
        for (var row : rows) {
            values.putAll(row.values());
            cells.putAll(row.effectiveCells());
        }
        return new ExecutionRow(values, cells);
    }

    private static List<List<ExecutionRow>> cartesian(List<List<ExecutionRow>> groups) {
        if (groups.isEmpty()) return List.of(List.of());
        List<List<ExecutionRow>> current = List.of(List.of());
        for (var group : groups) {
            var next = new ArrayList<List<ExecutionRow>>();
            for (var prefix : current) {
                for (var item : group) {
                    var combined = new ArrayList<>(prefix);
                    combined.add(item);
                    next.add(List.copyOf(combined));
                }
            }
            current = next;
        }
        return current;
    }

    private int compareValues(Object left, Object right) {
        if (left == right) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        if (left instanceof Number && right instanceof Number) {
            try {
                return new BigDecimal(left.toString()).compareTo(new BigDecimal(right.toString()));
            } catch (NumberFormatException ignored) {
                // Fall through to comparable/string comparison.
            }
        }
        if (left instanceof Comparable<?> comparable) {
            try {
                @SuppressWarnings("unchecked")
                var typed = (Comparable<Object>) comparable;
                return typed.compareTo(right);
            } catch (ClassCastException ignored) {
                // Fall through to deterministic textual comparison.
            }
        }
        return left.toString().compareTo(right.toString());
    }

    private Object parseConstant(String value) {
        if (value == null || value.equals("null")) return null;
        if (value.equals("true")) return true;
        if (value.equals("false")) return false;
        try { return Integer.valueOf(value); } catch (NumberFormatException ignored) { }
        try { return Long.valueOf(value); } catch (NumberFormatException ignored) { }
        try { return new BigDecimal(value); } catch (NumberFormatException ignored) { }
        return value;
    }

    private FieldId fieldForColumn(EntityId entityId, com.foundgine.core.abstractions.ColumnId columnId) {
        return metadata.getEntity(entityId).effectiveFields().stream()
                .filter(x -> x.column() != null && x.column().columnId().equals(columnId))
                .map(FieldMetadata::id)
                .findFirst()
                .orElseThrow(() -> new IllegalStateException(
                        "No semantic field maps " + entityId + " to column " + columnId + "."));
    }

    private static boolean toBoolean(Object value) {
        if (value instanceof Boolean b) return b;
        if (value instanceof Number n) return n.doubleValue() != 0d;
        if (value == null) return false;
        return Boolean.parseBoolean(value.toString());
    }
}
