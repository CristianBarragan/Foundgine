package com.foundgine.providers.gateway.http;

import com.foundgine.providers.json.FoundgineJson;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Bounded wire representation of the supported canonical mutation graph subset. Entity and field ids are unsigned
 * 64-bit values carried as their bit pattern in a {@code long}; unknown JSON members are rejected.
 */
public record FoundgineGatewayMutationRequest(List<Operation> operations) {

    /** {@code fields} keys are decimal field ids (kept as the wire strings so the mapper can validate them). */
    public record Operation(long entityId, String kind, Map<String, Object> fields, List<Long> conflictFields,
            List<Long> returnFields, Filter filter) { }

    public record Filter(String kind, Long fieldId, String operator, Object value, List<Filter> expressions) { }

    public static FoundgineGatewayMutationRequest fromJson(String json) {
        var root = GatewayJsonValues.object(FoundgineJson.parse(json), "request");
        GatewayJsonValues.allowOnly(root, "mutation request", Set.of("operations"));
        var raw = GatewayJsonValues.array(root, "operations");
        List<Operation> operations = null;
        if (raw != null) { operations = new ArrayList<>(); for (Object o : raw) operations.add(operation(o)); }
        return new FoundgineGatewayMutationRequest(operations);
    }

    private static Operation operation(Object raw) {
        if (raw == null) return null;
        var m = GatewayJsonValues.object(raw, "operation");
        GatewayJsonValues.allowOnly(m, "mutation operation", Set.of("entityId", "kind", "fields", "conflictFields", "returnFields", "filter"));
        Long entity = GatewayJsonValues.unsigned(m.get("entityId"), "entityId");
        Map<String, Object> fields = null;
        if (m.get("fields") != null) fields = new LinkedHashMap<>(GatewayJsonValues.object(m.get("fields"), "fields"));
        return new Operation(entity == null ? 0L : entity, GatewayJsonValues.string(m, "kind"), fields,
                ids(m, "conflictFields"), ids(m, "returnFields"), m.get("filter") == null ? null : filter(m.get("filter"), 0));
    }

    private static List<Long> ids(Map<String, Object> m, String key) {
        var raw = GatewayJsonValues.array(m, key);
        if (raw == null) return null;
        var out = new ArrayList<Long>(raw.size());
        for (Object o : raw) {
            Long id = GatewayJsonValues.unsigned(o, key);
            if (id == null) throw new IllegalArgumentException("'" + key + "' entries must be unsigned 64-bit integers.");
            out.add(id);
        }
        return out;
    }

    private static Filter filter(Object raw, int depth) {
        if (raw == null) return null;
        if (depth > 64) throw new IllegalArgumentException("Mutation filter depth/node limit exceeded.");
        var m = GatewayJsonValues.object(raw, "filter");
        GatewayJsonValues.allowOnly(m, "mutation filter", Set.of("kind", "fieldId", "operator", "value", "expressions"));
        List<Filter> expressions = null;
        var rawExpressions = GatewayJsonValues.array(m, "expressions");
        if (rawExpressions != null) { expressions = new ArrayList<>(); for (Object o : rawExpressions) expressions.add(filter(o, depth + 1)); }
        return new Filter(GatewayJsonValues.string(m, "kind"), GatewayJsonValues.unsigned(m.get("fieldId"), "fieldId"),
                GatewayJsonValues.string(m, "operator"), m.get("value"), expressions);
    }
}
