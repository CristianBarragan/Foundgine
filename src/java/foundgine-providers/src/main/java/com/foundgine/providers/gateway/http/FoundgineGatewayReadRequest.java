package com.foundgine.providers.gateway.http;

import com.foundgine.providers.json.FoundgineJson;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** Wire DTO for a bounded, provider-neutral read intent. Unknown JSON members are rejected. */
public record FoundgineGatewayReadRequest(String rootEntity, List<Selection> selections, Filter filter,
        List<Order> order, Integer limit, Integer offset, String after) {

    public record Selection(String field, String relationship, List<Selection> children) { }

    /** {@code value} is a normalized JSON value (Long, BigDecimal, String, Boolean, List, Map or null). */
    public record Filter(String kind, String field, String operator, Object value, String relationship,
            String quantifier, Filter predicate, List<Filter> expressions) { }

    public record Order(String field, String direction, List<String> relationshipPath, String aggregate) { }

    /** Parses the request body; any structural problem is an {@link IllegalArgumentException} (a 400 for hosts). */
    public static FoundgineGatewayReadRequest fromJson(String json) {
        var root = GatewayJsonValues.object(FoundgineJson.parse(json), "request");
        GatewayJsonValues.allowOnly(root, "read request", Set.of("rootEntity", "selections", "filter", "order", "limit", "offset", "after"));
        List<Selection> selections = null;
        var rawSelections = GatewayJsonValues.array(root, "selections");
        if (rawSelections != null) { selections = new ArrayList<>(); for (Object o : rawSelections) selections.add(selection(o)); }
        List<Order> order = null;
        var rawOrder = GatewayJsonValues.array(root, "order");
        if (rawOrder != null) { order = new ArrayList<>(); for (Object o : rawOrder) order.add(order(o)); }
        return new FoundgineGatewayReadRequest(GatewayJsonValues.string(root, "rootEntity"), selections,
                root.get("filter") == null ? null : filter(root.get("filter"), 0),
                order, GatewayJsonValues.integer(root, "limit"), GatewayJsonValues.integer(root, "offset"),
                GatewayJsonValues.string(root, "after"));
    }

    private static Selection selection(Object raw) {
        if (raw == null) return null;
        var m = GatewayJsonValues.object(raw, "selection");
        GatewayJsonValues.allowOnly(m, "selection", Set.of("field", "relationship", "children"));
        List<Selection> children = null;
        var rawChildren = GatewayJsonValues.array(m, "children");
        if (rawChildren != null) { children = new ArrayList<>(); for (Object o : rawChildren) children.add(selection(o)); }
        return new Selection(GatewayJsonValues.string(m, "field"), GatewayJsonValues.string(m, "relationship"), children);
    }

    private static Filter filter(Object raw, int depth) {
        if (raw == null) return null;
        // Bound recursion while parsing; the mapper enforces the configured (smaller) limits.
        if (depth > 64) throw new IllegalArgumentException("Read filter limits exceeded.");
        var m = GatewayJsonValues.object(raw, "filter");
        GatewayJsonValues.allowOnly(m, "filter", Set.of("kind", "field", "operator", "value", "relationship", "quantifier", "predicate", "expressions"));
        List<Filter> expressions = null;
        var rawExpressions = GatewayJsonValues.array(m, "expressions");
        if (rawExpressions != null) { expressions = new ArrayList<>(); for (Object o : rawExpressions) expressions.add(filter(o, depth + 1)); }
        return new Filter(GatewayJsonValues.string(m, "kind"), GatewayJsonValues.string(m, "field"),
                GatewayJsonValues.string(m, "operator"), m.get("value"), GatewayJsonValues.string(m, "relationship"),
                GatewayJsonValues.string(m, "quantifier"), m.get("predicate") == null ? null : filter(m.get("predicate"), depth + 1), expressions);
    }

    private static Order order(Object raw) {
        if (raw == null) return null;
        var m = GatewayJsonValues.object(raw, "order");
        GatewayJsonValues.allowOnly(m, "order", Set.of("field", "direction", "relationshipPath", "aggregate"));
        List<String> path = null;
        var rawPath = GatewayJsonValues.array(m, "relationshipPath");
        if (rawPath != null) {
            path = new ArrayList<>();
            for (Object o : rawPath) { if (!(o instanceof String s)) throw new IllegalArgumentException("relationshipPath entries must be strings."); path.add(s); }
        }
        return new Order(GatewayJsonValues.string(m, "field"), GatewayJsonValues.string(m, "direction"), path, GatewayJsonValues.string(m, "aggregate"));
    }

}
