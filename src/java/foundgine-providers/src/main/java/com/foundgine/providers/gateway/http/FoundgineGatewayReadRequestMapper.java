package com.foundgine.providers.gateway.http;

import com.foundgine.core.semantic.intent.ReadAndFilter;
import com.foundgine.core.semantic.intent.ReadFieldFilter;
import com.foundgine.core.semantic.intent.ReadFilter;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadOrFilter;
import com.foundgine.core.semantic.intent.ReadOrder;
import com.foundgine.core.semantic.intent.ReadRelationshipFilter;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrderAggregate;
import com.foundgine.core.semantic.query.SemanticRelationshipQuantifier;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import java.util.ArrayList;
import java.util.List;

/** Validates a wire read request against the HTTP limits and maps it to the provider-neutral {@link ReadIntent}. */
public final class FoundgineGatewayReadRequestMapper {
    private FoundgineGatewayReadRequestMapper() { }

    private static final class Counter { int value; }

    public static ReadIntent toIntent(FoundgineGatewayReadRequest request, FoundgineGatewayHttpOptions limits) {
        if (request == null) throw new IllegalArgumentException("request is required");
        limits.validateReadLimits();
        GatewayJsonValues.required(request.rootEntity(), "rootEntity");
        if (request.selections() == null || request.selections().isEmpty())
            throw new IllegalArgumentException("At least one selection is required.");
        var selectionCount = new Counter();
        var selections = new ArrayList<ReadSelection>(request.selections().size());
        for (var s : request.selections()) selections.add(mapSelection(s, 1, selectionCount, limits));
        if (request.limit() != null && request.limit() < 0 || request.offset() != null && request.offset() < 0)
            throw new IllegalArgumentException("limit and offset cannot be negative.");
        int effectiveLimit = request.limit() != null ? request.limit() : limits.defaultReadLimit;
        if (effectiveLimit < 1 || effectiveLimit > limits.maxReadLimit)
            throw new IllegalArgumentException("limit must be between 1 and " + limits.maxReadLimit + ".");
        if (request.order() != null && request.order().size() > 128)
            throw new IllegalArgumentException("order cannot contain more than 128 entries.");
        if (request.after() != null && request.after().length() > 4096)
            throw new IllegalArgumentException("after cursor exceeds the maximum length.");
        var filterCount = new Counter();
        ReadFilter filter = request.filter() == null ? null : mapFilter(request.filter(), 1, filterCount, limits);
        List<ReadOrder> order = null;
        if (request.order() != null) { order = new ArrayList<>(); for (var o : request.order()) order.add(mapOrder(o)); }
        return new ReadIntent(request.rootEntity(), selections, filter, order, effectiveLimit, request.offset(), request.after(), null);
    }

    private static ReadSelection mapSelection(FoundgineGatewayReadRequest.Selection value, int depth, Counter count,
            FoundgineGatewayHttpOptions limits) {
        if (value == null) throw new IllegalArgumentException("Selection entries cannot be null.");
        if (depth > limits.maxSelectionDepth || ++count.value > limits.maxSelectionCount)
            throw new IllegalArgumentException("Read selection limits exceeded.");
        boolean field = value.field() != null && !value.field().isBlank();
        boolean relationship = value.relationship() != null && !value.relationship().isBlank();
        if (field == relationship) throw new IllegalArgumentException("Each selection must specify exactly one of field or relationship.");
        List<ReadSelection> children = null;
        if (value.children() != null) {
            children = new ArrayList<>(value.children().size());
            for (var child : value.children()) children.add(mapSelection(child, depth + 1, count, limits));
        }
        return new ReadSelection(field ? value.field() : null, relationship ? value.relationship() : null, children);
    }

    private static ReadFilter mapFilter(FoundgineGatewayReadRequest.Filter value, int depth, Counter count,
            FoundgineGatewayHttpOptions limits) {
        if (value == null) throw new IllegalArgumentException("Filter entries cannot be null.");
        if (depth > limits.maxFilterDepth || ++count.value > limits.maxFilterNodes)
            throw new IllegalArgumentException("Read filter limits exceeded.");
        String kind = value.kind() == null ? null : value.kind().trim().toLowerCase(java.util.Locale.ROOT);
        if (kind == null) throw new IllegalArgumentException("Unsupported filter kind.");
        return switch (kind) {
            case "field" -> new ReadFieldFilter(GatewayJsonValues.required(value.field(), "filter.field"),
                    GatewayJsonValues.parseEnum(SemanticFilterOperator.class, value.operator(), "filter.operator"),
                    GatewayJsonValues.normalize(value.value(), 0, limits.maxJsonValueDepth, "filter"));
            case "relationship" -> new ReadRelationshipFilter(GatewayJsonValues.required(value.relationship(), "filter.relationship"),
                    GatewayJsonValues.parseEnum(SemanticRelationshipQuantifier.class, value.quantifier(), "filter.quantifier"),
                    value.predicate() == null ? failPredicate() : mapFilter(value.predicate(), depth + 1, count, limits));
            case "and" -> new ReadAndFilter(mapExpressions(value.expressions(), depth, count, limits));
            case "or" -> new ReadOrFilter(mapExpressions(value.expressions(), depth, count, limits));
            default -> throw new IllegalArgumentException("Unsupported filter kind.");
        };
    }

    private static ReadFilter failPredicate() { throw new IllegalArgumentException("Relationship filter requires predicate."); }

    private static List<ReadFilter> mapExpressions(List<FoundgineGatewayReadRequest.Filter> values, int depth, Counter count,
            FoundgineGatewayHttpOptions limits) {
        if (values == null || values.isEmpty()) throw new IllegalArgumentException("AND/OR filters require expressions.");
        var result = new ArrayList<ReadFilter>(values.size());
        for (var v : values) result.add(mapFilter(v, depth + 1, count, limits));
        return result;
    }

    private static ReadOrder mapOrder(FoundgineGatewayReadRequest.Order value) {
        if (value == null) throw new IllegalArgumentException("Order entries cannot be null.");
        return new ReadOrder(GatewayJsonValues.required(value.field(), "order.field"),
                GatewayJsonValues.parseEnum(SemanticSortDirection.class, value.direction(), "order.direction"),
                value.relationshipPath(),
                value.aggregate() == null || value.aggregate().isBlank() ? SemanticOrderAggregate.NONE
                        : GatewayJsonValues.parseEnum(SemanticOrderAggregate.class, value.aggregate(), "order.aggregate"));
    }
}
