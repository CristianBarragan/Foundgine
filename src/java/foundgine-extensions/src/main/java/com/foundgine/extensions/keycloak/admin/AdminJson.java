package com.foundgine.extensions.keycloak.admin;

import com.foundgine.providers.json.FoundgineJson;
import java.util.List;
import java.util.Map;

/** Minimal JSON writer for the Admin API request bodies (maps, lists, strings, numbers, booleans, null). */
final class AdminJson {
    private AdminJson() { }

    static String write(Object value) {
        var sb = new StringBuilder();
        write(sb, value);
        return sb.toString();
    }

    private static void write(StringBuilder sb, Object value) {
        if (value == null) sb.append("null");
        else if (value instanceof String s) FoundgineJson.writeString(sb, s);
        else if (value instanceof Boolean || value instanceof Number) sb.append(value);
        else if (value instanceof Map<?, ?> m) {
            sb.append('{');
            boolean first = true;
            for (var e : m.entrySet()) {
                if (!first) sb.append(',');
                first = false;
                FoundgineJson.writeString(sb, String.valueOf(e.getKey()));
                sb.append(':');
                write(sb, e.getValue());
            }
            sb.append('}');
        } else if (value instanceof List<?> l) {
            sb.append('[');
            for (int i = 0; i < l.size(); i++) {
                if (i > 0) sb.append(',');
                write(sb, l.get(i));
            }
            sb.append(']');
        } else throw new IllegalArgumentException("Unsupported JSON value: " + value.getClass().getName());
    }
}
