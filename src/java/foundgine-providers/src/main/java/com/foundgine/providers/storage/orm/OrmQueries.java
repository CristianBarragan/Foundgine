package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.AuthorizationPredicateKind;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionContextKeys;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.ExecutionPageInfo;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticSortDirection;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Validation, lowering (filters, authorization, keyset cursors) and the paged read of ONE plan
 * node. Everything fails closed: unknown shapes, unmapped fields, missing context values and
 * unorderable cursor keys throw instead of being guessed.
 */
final class OrmQueries {
    static final int KEY_CHUNK_SIZE = 500;

    private OrmQueries() {}

    record Fetch(List<Map<FieldId, Object>> rows, ExecutionPageInfo pageInfo, List<List<Object>> privateOrderValues) {}

    // ------------------------------------------------------------------ validation

    static void validate(OrmRegistry registry, OrmEntityMapping map, ExecutionIRNode node, boolean isRoot) {
        var expected = isRoot ? ExecutionOperation.SCAN : ExecutionOperation.TRAVERSE;
        if (node.operation() != expected) {
            throw new UnsupportedOperationException(
                    "ORM provider: operation '"
                            + node.operation()
                            + "' is not supported for a "
                            + (isRoot ? "root" : "child")
                            + " node.");
        }
        if (node.viaConnection() != null) {
            throw new UnsupportedOperationException(
                    "ORM provider: connections are not supported.");
        }
        if (!isRoot && node.viaRelationship() == null) {
            throw new IllegalStateException(
                    "Child execution node " + node.id() + " has no relationship identity.");
        }
        if (node.fields().isEmpty()) {
            throw new IllegalStateException(
                    "Execution node "
                            + node.id()
                            + " selects no fields after semantic authorization.");
        }
        for (FieldId f : node.fields()) map.field(f);

        var options = node.queryOptions();
        if (options != null) {
            if ((options.limit() != null && options.limit() < 0)
                    || (options.offset() != null && options.offset() < 0)) {
                throw new IllegalStateException("Query limit and offset must be non-negative.");
            }
            if (!isRoot
                    && (options.limit() != null
                            || options.offset() != null
                            || options.hasCursor())) {
                throw new UnsupportedOperationException(
                        "ORM provider: limit, offset and cursor paging apply to the root node only"
                                + " (per-parent child paging has no translatable form).");
            }
            for (SemanticOrderTerm term : options.effectiveOrder()) {
                if (!term.isAggregate() && !term.isRootField()) {
                    throw new UnsupportedOperationException(
                            "ORM provider: only node-local or one-hop aggregate ordering is supported.");
                }
                if (term.isAggregate()) {
                    if (term.effectivePath().size() != 1)
                        throw new UnsupportedOperationException("ORM provider: aggregate ordering supports exactly one relationship hop.");
                    var aggregateChild = node.children().stream()
                            .filter(c -> term.effectivePath().get(0).equals(c.viaRelationship()))
                            .findFirst().orElseThrow(() -> new UnsupportedOperationException(
                                    "Aggregate ordering requires a matching authorized child plan."));
                    var aggregateTarget = registry.map(aggregateChild.entityId());
                    registry.link(term.effectivePath().get(0), map.entityId(), aggregateTarget.entityId());
                    if (term.aggregate() != com.foundgine.core.semantic.query.SemanticOrderAggregate.COUNT) {
                        var value = aggregateTarget.field(term.field());
                        if (!value.isOrderable()) throw new UnsupportedOperationException("Aggregate ordering field is not portable.");
                    }
                } else map.field(term.field());
            }
            boolean aggregateOrder = options.effectiveOrder().stream().anyMatch(SemanticOrderTerm::isAggregate);
            if (options.hasCursor() && aggregateOrder) {
                if (map.primaryKeys().isEmpty())
                    throw new UnsupportedOperationException("ORM provider: aggregate cursor paging requires a declared primary key.");
                for (var primaryKey : map.primaryKeys()) {
                    if (!primaryKey.isOrderable())
                        throw new UnsupportedOperationException("ORM provider: aggregate cursor primary keys must have portable ordering.");
                }
            }
            if (options.hasCursor() && !aggregateOrder) {
                if (options.offset() != null && options.offset() > 0) {
                    throw new UnsupportedOperationException(
                            "ORM provider: cursor paging cannot be combined with an offset.");
                }
                for (var key : orderKeys(map, options.effectiveOrder())) {
                    var b = map.field(key.field());
                    if (!b.isOrderable()) {
                        throw new UnsupportedOperationException(
                                "ORM provider: field '"
                                        + map.name()
                                        + "."
                                        + b.name()
                                        + "' ("
                                        + b.type().getSimpleName()
                                        + ") has no portable ordering, so it cannot take part in"
                                        + " cursor paging.");
                    }
                }
            }
            if (options.filter() != null) lowerFilter(map, options.filter());
        }
        if (node.authorization() != null) lowerAuthorization(map, node.authorization(), null);
    }

    // ------------------------------------------------------------------ filters

    static OrmPredicate lowerFilter(OrmEntityMapping map, SemanticFilterExpression filter) {
        if (filter instanceof SemanticAndFilter and) {
            OrmPredicate acc = new OrmPredicate.Const(true);
            for (var e : and.expressions()) acc = OrmPredicate.and(acc, lowerFilter(map, e));
            return acc;
        }
        if (filter instanceof SemanticOrFilter or) {
            OrmPredicate acc = new OrmPredicate.Const(false);
            for (var e : or.expressions()) acc = OrmPredicate.or(acc, lowerFilter(map, e));
            return acc;
        }
        if (filter instanceof SemanticFieldFilter f) {
            var b = map.field(f.field());
            switch (f.operator()) {
                case EQ:
                    return new OrmPredicate.Compare(
                            f.field(), OrmPredicate.Op.EQ, OrmValues.coerce(f.value(), b.type()));
                case NEQ:
                    return new OrmPredicate.Compare(
                            f.field(), OrmPredicate.Op.NEQ, OrmValues.coerce(f.value(), b.type()));
                case IN:
                    {
                        var values = new ArrayList<Object>();
                        for (Object v : asList(f.value())) {
                            values.add(OrmValues.coerce(v, b.type()));
                        }
                        return values.isEmpty()
                                ? new OrmPredicate.Const(false)
                                : new OrmPredicate.In(f.field(), values);
                    }
                default:
                    {
                        if (!b.isOrderable()) {
                            throw new UnsupportedOperationException(
                                    "ORM provider: field '"
                                            + b.name()
                                            + "' does not support ordering comparisons.");
                        }
                        if (f.value() == null) {
                            throw new IllegalArgumentException(
                                    "Comparison operator " + f.operator() + " requires a value.");
                        }
                        var op =
                                switch (f.operator()) {
                                    case GT -> OrmPredicate.Op.GT;
                                    case GTE -> OrmPredicate.Op.GTE;
                                    case LT -> OrmPredicate.Op.LT;
                                    default -> OrmPredicate.Op.LTE;
                                };
                        return new OrmPredicate.Compare(
                                f.field(), op, OrmValues.coerce(f.value(), b.type()));
                    }
            }
        }
        throw new UnsupportedOperationException(
                "ORM provider: filter '" + filter.getClass().getSimpleName() + "' is not supported.");
    }

    private static List<Object> asList(Object value) {
        if (value instanceof Collection<?> c) return new ArrayList<>(c);
        if (value != null && value.getClass().isArray()) {
            int n = java.lang.reflect.Array.getLength(value);
            var list = new ArrayList<Object>(n);
            for (int i = 0; i < n; i++) list.add(java.lang.reflect.Array.get(value, i));
            return list;
        }
        throw new IllegalArgumentException("An IN filter requires a collection value.");
    }

    // ------------------------------------------------------------------ authorization

    /** Operand of an authorization comparison: a resource field or a resolved value. */
    private record Operand(OrmFieldBinding field, Object value, boolean unresolved) {
        static Operand ofField(OrmFieldBinding f) {
            return new Operand(f, null, false);
        }

        static Operand ofValue(Object v) {
            return new Operand(null, v, false);
        }
    }

    /** {@code context == null} validates shape only (context operands become unresolved). */
    static OrmPredicate lowerAuthorization(
            OrmEntityMapping map, AuthorizationPredicate p, ExecutionContext context) {
        return switch (p.kind()) {
            case AND ->
                    OrmPredicate.and(
                            lowerAuthorization(map, req(p.left()), context),
                            lowerAuthorization(map, req(p.right()), context));
            case OR ->
                    OrmPredicate.or(
                            lowerAuthorization(map, req(p.left()), context),
                            lowerAuthorization(map, req(p.right()), context));
            case NOT -> OrmPredicate.not(lowerAuthorization(map, req(p.left()), context));
            case EQUAL -> compare(map, p, context, false);
            case NOT_EQUAL -> compare(map, p, context, true);
            default -> {
                // A bare boolean operand: resource member, context value or constant.
                var o = operand(map, p, context);
                if (o.unresolved()) yield new OrmPredicate.Const(true);
                if (o.field() != null) {
                    yield new OrmPredicate.Compare(o.field().id(), OrmPredicate.Op.EQ, true);
                }
                yield new OrmPredicate.Const(toBoolean(o.value()));
            }
        };
    }

    private static AuthorizationPredicate req(AuthorizationPredicate p) {
        return Objects.requireNonNull(p, "Authorization predicate operand is missing.");
    }

    private static OrmPredicate compare(
            OrmEntityMapping map,
            AuthorizationPredicate p,
            ExecutionContext context,
            boolean negate) {
        var left = operand(map, req(p.left()), context);
        var right = operand(map, req(p.right()), context);
        if (left.unresolved() || right.unresolved()) return new OrmPredicate.Const(true);
        var op = negate ? OrmPredicate.Op.NEQ : OrmPredicate.Op.EQ;
        if (left.field() != null && right.field() != null) {
            throw new UnsupportedOperationException(
                    "Authorization comparisons between two resource members are not supported.");
        }
        if (left.field() != null) {
            return new OrmPredicate.Compare(
                    left.field().id(), op, OrmValues.coerce(right.value(), left.field().type()));
        }
        if (right.field() != null) {
            return new OrmPredicate.Compare(
                    right.field().id(), op, OrmValues.coerce(left.value(), right.field().type()));
        }
        boolean equal = OrmValues.valueEquals(left.value(), right.value());
        return new OrmPredicate.Const(negate != equal);
    }

    private static Operand operand(
            OrmEntityMapping map, AuthorizationPredicate p, ExecutionContext context) {
        switch (p.kind()) {
            case CONSTANT:
                return Operand.ofValue(parseConstant(p.value()));
            case CONTEXT_PARAMETER:
                return contextValue(p.name(), context);
            case MEMBER_ACCESS:
                {
                    var target = req(p.left());
                    if (target.kind() == AuthorizationPredicateKind.RESOURCE_PARAMETER) {
                        String name = Objects.requireNonNull(p.name(), "Resource member has no name.");
                        return Operand.ofField(
                                map.fieldByName(name)
                                        .orElseThrow(
                                                () ->
                                                        new IllegalStateException(
                                                                "Authorization resource member '"
                                                                        + map.name()
                                                                        + "."
                                                                        + name
                                                                        + "' has no field mapping.")));
                    }
                    if (target.kind() == AuthorizationPredicateKind.CONTEXT_PARAMETER) {
                        return contextValue(
                                (target.name() == null ? "" : target.name())
                                        + "."
                                        + (p.name() == null ? "" : p.name()),
                                context);
                    }
                    throw new UnsupportedOperationException(
                            "Only context and resource member authorization is supported.");
                }
            default:
                throw new UnsupportedOperationException(
                        "Authorization node '" + p.kind() + "' is not supported as an operand.");
        }
    }

    private static Operand contextValue(String path, ExecutionContext context) {
        if (context == null) return new Operand(null, null, true);
        var value = context.tryGetValue(path);
        if (value.isEmpty()) {
            throw new IllegalStateException(
                    "Authorization context value '" + path + "' is missing.");
        }
        return Operand.ofValue(value.get());
    }

    private static Object parseConstant(String value) {
        if (value == null || value.equals("null")) return null;
        if (value.equals("true")) return true;
        if (value.equals("false")) return false;
        try {
            return Long.valueOf(value);
        } catch (NumberFormatException ignored) {
            // fall through
        }
        try {
            return new BigDecimal(value);
        } catch (NumberFormatException ignored) {
            // fall through
        }
        return value;
    }

    private static boolean toBoolean(Object v) {
        if (v instanceof Boolean b) return b;
        if (v instanceof Number n) return n.doubleValue() != 0d;
        return v != null && Boolean.parseBoolean(v.toString());
    }

    // ------------------------------------------------------------------ ordering / cursors

    /** Requested order plus the primary key as a total-order tiebreaker. */
    static List<SemanticOrderTerm> orderKeys(OrmEntityMapping map, List<SemanticOrderTerm> terms) {
        var result = new ArrayList<SemanticOrderTerm>(terms);
        for (var pk : map.primaryKeys()) {
            if (result.stream().anyMatch(t -> t.field().equals(pk.id()))) continue;
            result.add(new SemanticOrderTerm(pk.id(), SemanticSortDirection.ASC));
        }
        return result;
    }

    static OrmPredicate keyset(
            OrmEntityMapping map, List<SemanticOrderTerm> terms, List<String> cursorValues) {
        if (cursorValues.size() != terms.size()) {
            throw new IllegalStateException(
                    "The pagination cursor does not match the ordering of this query.");
        }
        var typed = new ArrayList<Object>();
        for (int i = 0; i < terms.size(); i++) {
            var b = map.field(terms.get(i).field());
            if (cursorValues.get(i) == null) {
                throw new UnsupportedOperationException(
                        "ORM provider: cursor paging cannot position on a null '"
                                + b.name()
                                + "' value.");
            }
            typed.add(OrmValues.coerce(cursorValues.get(i), b.type()));
        }
        OrmPredicate result = null;
        for (int i = 0; i < terms.size(); i++) {
            boolean desc = terms.get(i).direction() == SemanticSortDirection.DESC;
            OrmPredicate branch =
                    new OrmPredicate.Compare(
                            terms.get(i).field(),
                            desc ? OrmPredicate.Op.LT : OrmPredicate.Op.GT,
                            typed.get(i));
            for (int j = i - 1; j >= 0; j--) {
                branch =
                        OrmPredicate.and(
                                new OrmPredicate.Compare(
                                        terms.get(j).field(), OrmPredicate.Op.EQ, typed.get(j)),
                                branch);
            }
            result = result == null ? branch : OrmPredicate.or(result, branch);
        }
        return result == null ? new OrmPredicate.Const(true) : result;
    }

    /** Stable cursor identity derived inside the provider from the current, resolved plan. */
    private static String aggregateCursorPlanIdentity(OrmRegistry registry, OrmEntityMapping map,
            ExecutionIRNode node, List<OrmOrder> order, ExecutionContext context) {
        // Length-prefix every atom: delimiters inside identifiers, values or predicate rendering
        // must never make two different plan descriptions serialize to the same byte sequence.
        var b = new StringBuilder();
        appendPlanAtom(b, "foundgine-orm-aggregate-cursor");
        appendPlanAtom(b, Integer.toString(OrmAggregateCursorBinding.CURSOR_SCHEMA_VERSION));
        appendPlanAtom(b, Long.toString(map.entityId().value()));
        appendPlanAtom(b, "root-fields");
        for (var field : map.fields()) {
            appendPlanAtom(b, Long.toString(field.id().value()));
            appendPlanAtom(b, field.name());
            appendPlanAtom(b, field.type().getName());
            appendPlanAtom(b, Boolean.toString(field.primaryKey()));
            appendPlanAtom(b, field.column() == null ? "" : Long.toString(field.column().value()));
        }
        appendPlanAtom(b, "selected-fields");
        for (FieldId field : node.fields()) appendPlanAtom(b, Long.toString(field.value()));
        if (node.queryOptions() != null && node.queryOptions().filter() != null) {
            appendPlanAtom(b, "root-filter");
            appendPlanAtom(b, lowerFilter(map, node.queryOptions().filter()).toString());
        }
        if (node.authorization() != null) {
            appendPlanAtom(b, "root-authorization");
            appendPlanAtom(b, lowerAuthorization(map, node.authorization(), context).toString());
        }
        for (ExecutionIRNode child : node.children()) {
            var childMap = registry.map(child.entityId());
            appendPlanAtom(b, "child");
            appendPlanAtom(b, Long.toString(child.entityId().value()));
            appendPlanAtom(b, child.viaRelationship() == null ? "" : Long.toString(child.viaRelationship().value()));
            for (var field : childMap.fields()) {
                appendPlanAtom(b, Long.toString(field.id().value()));
                appendPlanAtom(b, field.name());
                appendPlanAtom(b, field.type().getName());
                appendPlanAtom(b, Boolean.toString(field.primaryKey()));
                appendPlanAtom(b, field.column() == null ? "" : Long.toString(field.column().value()));
            }
            for (FieldId field : child.fields()) appendPlanAtom(b, Long.toString(field.value()));
            if (child.authorization() != null)
                appendPlanAtom(b, lowerAuthorization(childMap, child.authorization(), context).toString());
            if (child.queryOptions() != null && child.queryOptions().filter() != null)
                appendPlanAtom(b, lowerFilter(childMap, child.queryOptions().filter()).toString());
        }
        for (OrmOrder term : order) {
            appendPlanAtom(b, "order");
            appendPlanAtom(b, Long.toString(term.field().value()));
            appendPlanAtom(b, Boolean.toString(term.descending()));
            appendPlanAtom(b, term.aggregate().name());
            if (term.isAggregate()) {
                appendPlanAtom(b, Long.toString(term.target().entityId().value()));
                appendPlanAtom(b, Long.toString(term.link().parentField().value()));
                appendPlanAtom(b, Long.toString(term.link().childField().value()));
                appendPlanAtom(b, term.authorization().toString());
            }
        }
        return b.toString();
    }

    private static void appendPlanAtom(StringBuilder target, String value) {
        String atom = value == null ? "" : value;
        target.append(atom.length()).append(':').append(atom);
    }

    // ------------------------------------------------------------------ the read

    static Fetch fetch(
            OrmRegistry registry, OrmEntityMapping map,
            ExecutionIRNode node,
            boolean isRoot,
            Collection<FieldId> extraFields,
            FieldId keyField,
            List<Object> keys,
            IOrmQuerySource source,
            ExecutionContext context) {
        validate(registry, map, node, isRoot);
        var options = node.queryOptions();
        List<SemanticOrderTerm> order =
                options == null ? List.of() : options.effectiveOrder();

        Integer limit = isRoot && options != null ? options.limit() : null;
        Integer offset = isRoot && options != null ? options.offset() : null;
        if (isRoot) {
            var l = context.tryGetValue(ExecutionContextKeys.PAGINATION_LIMIT);
            if (l.isPresent()) limit = ((Number) l.get()).intValue();
            var o = context.tryGetValue(ExecutionContextKeys.PAGINATION_OFFSET);
            if (o.isPresent()) offset = ((Number) o.get()).intValue();
        }

        String cursor = isRoot && options != null && options.hasCursor() ? options.after() : null;
        boolean paged = (limit != null && limit > 0) || (offset != null && offset > 0) || cursor != null;
        boolean aggregateOrder = order.stream().anyMatch(SemanticOrderTerm::isAggregate);
        if (aggregateOrder && offset != null && offset > 0)
            throw new UnsupportedOperationException("ORM provider: aggregate cursor paging cannot be combined with offset paging.");
        List<SemanticOrderTerm> orderKeys = paged && !aggregateOrder ? orderKeys(map, order) : order;

        var read = new ArrayList<FieldId>(node.fields());
        for (FieldId f : extraFields) if (!read.contains(f)) read.add(f);
        boolean forward = limit != null && limit > 0 && offset == null;
        if (cursor != null || forward) {
            for (var t : orderKeys) if (!t.isAggregate() && !read.contains(t.field())) read.add(t.field());
            if (aggregateOrder) for (var pk : map.primaryKeys()) if (!read.contains(pk.id())) read.add(pk.id());
        }

        OrmPredicate keysetPredicate =
                cursor == null || aggregateOrder ? null : keyset(map, orderKeys, OrmCursor.decode(cursor));

        if (paged && orderKeys.stream().noneMatch(t -> map.field(t.field()).primaryKey())) {
            throw new IllegalStateException(
                    "Paging '" + map.name() + "' needs a deterministic order but no primary key is declared.");
        }
        var orderBy = new ArrayList<OrmOrder>();
        for (var t : orderKeys) {
            boolean descending = t.direction() == SemanticSortDirection.DESC;
            if (!t.isAggregate()) {
                orderBy.add(new OrmOrder(t.field(), descending));
                continue;
            }
            if (t.effectivePath().size() != 1)
                throw new UnsupportedOperationException("ORM provider: aggregate ordering supports exactly one relationship hop.");
            var relationship = t.effectivePath().get(0);
            var childNode = node.children().stream()
                    .filter(c -> relationship.equals(c.viaRelationship()))
                    .findFirst().orElseThrow(() -> new UnsupportedOperationException(
                            "Aggregate ordering requires a matching authorized child execution node."));
            var target = registry.map(childNode.entityId());
            var kind = switch (t.aggregate()) {
                case COUNT -> OrmOrder.AggregateKind.COUNT;
                case MIN -> OrmOrder.AggregateKind.MIN;
                case MAX -> OrmOrder.AggregateKind.MAX;
                case NONE -> OrmOrder.AggregateKind.NONE;
            };
            var link = registry.link(relationship, map.entityId(), target.entityId());
            var authorization = childNode.authorization() == null
                    ? new OrmPredicate.Const(true)
                    : lowerAuthorization(target, childNode.authorization(), context);
            orderBy.add(new OrmOrder(t.field(), descending, kind, target, link, authorization));
        }
        if (order.stream().anyMatch(SemanticOrderTerm::isAggregate)) {
            // Aggregate ties must have a deterministic total order even before cursor paging is enabled.
            for (var primaryKey : map.primaryKeys()) {
                if (orderBy.stream().noneMatch(o -> !o.isAggregate() && o.field().equals(primaryKey.id())))
                    orderBy.add(new OrmOrder(primaryKey.id(), false));
            }
            if (map.primaryKeys().isEmpty())
                throw new IllegalStateException("Aggregate ordering requires a declared primary key for deterministic ties.");
        }

        String aggregatePlanIdentity = aggregateOrder
                ? aggregateCursorPlanIdentity(registry, map, node, orderBy, context) : null;
        List<Object> aggregateContinuation = aggregateOrder && cursor != null
                ? OrmAggregateCursorPipeline.decodeOrdered(cursor, aggregatePlanIdentity, map, orderBy).orderedValues()
                : List.of();

        var chunks = new ArrayList<List<Object>>();
        if (keys == null) {
            chunks.add(null);
        } else {
            for (int i = 0; i < keys.size(); i += KEY_CHUNK_SIZE) {
                chunks.add(keys.subList(i, Math.min(keys.size(), i + KEY_CHUNK_SIZE)));
            }
        }

        var rows = new ArrayList<Map<FieldId, Object>>();
        var privateOrderValues = new ArrayList<List<Object>>();
        boolean hasNext = false;
        for (var chunk : chunks) {
            var where = new ArrayList<OrmPredicate>();
            // Authorization first and as its own clause: later clauses can only narrow it.
            if (node.authorization() != null) {
                where.add(lowerAuthorization(map, node.authorization(), context));
            }
            if (chunk != null) {
                var b = map.field(keyField);
                var typed = new ArrayList<Object>(chunk.size());
                for (Object k : chunk) typed.add(OrmValues.coerce(k, b.type()));
                where.add(new OrmPredicate.In(keyField, typed));
            }
            if (options != null && options.filter() != null) {
                where.add(lowerFilter(map, options.filter()));
            }
            if (keysetPredicate != null) where.add(keysetPredicate);

            Integer take = limit != null && limit > 0 ? (forward ? limit + 1 : limit) : null;
            Integer skip = offset != null && offset > 0 ? offset : null;
            var fetched = source.fetchWithPrivateOrderValues(
                    new OrmFetchRequest(map, read, where, orderBy, skip, take, aggregateContinuation));
            int count = fetched.rows().size();
            long aggregateOrderCount = orderBy.stream().filter(OrmOrder::isAggregate).count();
            if (aggregateOrderCount > 0 && fetched.privateOrderValues().size() != count) {
                throw new UnsupportedOperationException(
                        "ORM provider: aggregate ordering requires a source that transports private order values separately.");
            }
            if (forward && count > limit) {
                hasNext = true;
                count = limit;
            }
            for (int i = 0; i < count; i++) {
                var values = new LinkedHashMap<FieldId, Object>(read.size());
                for (FieldId f : read) values.put(f, fetched.rows().get(i).get(f));
                rows.add(values);
                if (!fetched.privateOrderValues().isEmpty())
                    privateOrderValues.add(fetched.privateOrderValues().get(i));
            }
        }

        ExecutionPageInfo page = null;
        if (forward || cursor != null) {
            String start = null;
            String end = null;
            if (!rows.isEmpty()) {
                var first = rows.get(0);
                var last = rows.get(rows.size() - 1);
                if (aggregateOrder) {
                    if (privateOrderValues.size() != rows.size())
                        throw new IllegalStateException("Aggregate cursor rows and private ordering tuples are misaligned.");
                    start = OrmAggregateCursorPipeline.nextPageCursor(aggregatePlanIdentity, map, orderBy,
                            privateOrderValues.get(0), first);
                    end = OrmAggregateCursorPipeline.nextPageCursor(aggregatePlanIdentity, map, orderBy,
                            privateOrderValues.get(privateOrderValues.size() - 1), last);
                } else {
                    start = OrmCursor.encode(orderKeys.stream().map(t -> first.get(t.field())).toList());
                    end = OrmCursor.encode(orderKeys.stream().map(t -> last.get(t.field())).toList());
                }
            }
            page = new ExecutionPageInfo(start, end, hasNext, cursor != null);
        }
        return new Fetch(rows, page, List.copyOf(privateOrderValues));
    }
}
