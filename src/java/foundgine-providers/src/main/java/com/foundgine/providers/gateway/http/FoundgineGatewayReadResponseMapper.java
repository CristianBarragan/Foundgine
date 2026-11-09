package com.foundgine.providers.gateway.http;

import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.providers.json.FoundgineJson;
import java.lang.reflect.Array;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.time.temporal.TemporalAccessor;
import java.util.ArrayList;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/** Maps an {@link ExecutionResult} to the gateway wire response and serializes it without reflection. */
public final class FoundgineGatewayReadResponseMapper {
    private static final int MAX_DEPTH = 16;
    private FoundgineGatewayReadResponseMapper() { }

    public static FoundgineGatewayReadResponse map(ExecutionResult result) {
        var rows = new ArrayList<FoundgineGatewayReadResponse.Row>(result.rows().size());
        for (var row : result.rows()) {
            var values = new LinkedHashMap<String, Object>();
            for (var pair : row.values().entrySet()) values.put(pair.getKey(), toJsonValue(pair.getValue(), 0));
            rows.add(new FoundgineGatewayReadResponse.Row(values));
        }
        var page = result.pageInfo() == null ? null : new FoundgineGatewayReadResponse.PageInfo(
                result.pageInfo().startCursor(), result.pageInfo().endCursor(),
                result.pageInfo().hasNextPage(), result.pageInfo().hasPreviousPage());
        return new FoundgineGatewayReadResponse(rows, page);
    }

    public static String toJson(FoundgineGatewayReadResponse response) {
        var sb = new StringBuilder(256);
        sb.append("{\"rows\":[");
        boolean first = true;
        for (var row : response.rows()) {
            if (!first) sb.append(',');
            first = false;
            sb.append("{\"values\":");
            writeValue(sb, row.values());
            sb.append('}');
        }
        sb.append("],\"pageInfo\":");
        var p = response.pageInfo();
        if (p == null) sb.append("null");
        else {
            sb.append("{\"startCursor\":"); FoundgineJson.writeNullableString(sb, p.startCursor());
            sb.append(",\"endCursor\":"); FoundgineJson.writeNullableString(sb, p.endCursor());
            sb.append(",\"hasNextPage\":").append(p.hasNextPage()).append(",\"hasPreviousPage\":").append(p.hasPreviousPage()).append('}');
        }
        return sb.append('}').toString();
    }

    static Object toJsonValue(Object value, int depth) {
        if (depth > MAX_DEPTH) throw new UnsupportedOperationException("Execution result value nesting exceeds the gateway limit.");
        if (value == null || value instanceof String || value instanceof Boolean) return value;
        if (value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long
                || value instanceof BigInteger || value instanceof BigDecimal) return value;
        if (value instanceof Float f) { requireFinite(f); return f; }
        if (value instanceof Double d) { requireFinite(d); return d; }
        if (value instanceof UUID u) return u.toString();
        if (value instanceof TemporalAccessor t) return t.toString();
        if (value instanceof byte[] bytes) return Base64.getEncoder().encodeToString(bytes);
        if (value instanceof Enum<?> e) return e.name();
        if (value instanceof Map<?, ?> m) {
            var out = new LinkedHashMap<String, Object>();
            for (var e : m.entrySet()) {
                if (!(e.getKey() instanceof String key)) throw new UnsupportedOperationException("Gateway result dictionaries require string keys.");
                out.put(key, toJsonValue(e.getValue(), depth + 1));
            }
            return out;
        }
        if (value instanceof Iterable<?> items) {
            var out = new ArrayList<Object>();
            for (Object item : items) out.add(toJsonValue(item, depth + 1));
            return out;
        }
        if (value.getClass().isArray()) {
            var out = new ArrayList<Object>();
            for (int i = 0; i < Array.getLength(value); i++) out.add(toJsonValue(Array.get(value, i), depth + 1));
            return out;
        }
        throw new UnsupportedOperationException("Execution result type '" + value.getClass().getName() + "' has no gateway wire mapping.");
    }

    private static void requireFinite(double d) {
        if (!Double.isFinite(d)) throw new UnsupportedOperationException("Non-finite numbers have no gateway wire mapping.");
    }

    @SuppressWarnings("unchecked")
    private static void writeValue(StringBuilder sb, Object value) {
        if (value == null) sb.append("null");
        else if (value instanceof String s) FoundgineJson.writeString(sb, s);
        else if (value instanceof Boolean || value instanceof Number) sb.append(value instanceof BigDecimal b ? b.toPlainString() : value.toString());
        else if (value instanceof Map<?, ?> m) {
            sb.append('{');
            boolean first = true;
            for (var e : ((Map<String, Object>) m).entrySet()) {
                if (!first) sb.append(',');
                first = false;
                FoundgineJson.writeString(sb, e.getKey());
                sb.append(':');
                writeValue(sb, e.getValue());
            }
            sb.append('}');
        } else if (value instanceof List<?> l) {
            sb.append('[');
            for (int i = 0; i < l.size(); i++) { if (i > 0) sb.append(','); writeValue(sb, l.get(i)); }
            sb.append(']');
        } else throw new UnsupportedOperationException("Unsupported wire value " + value.getClass().getName() + ".");
    }
}
