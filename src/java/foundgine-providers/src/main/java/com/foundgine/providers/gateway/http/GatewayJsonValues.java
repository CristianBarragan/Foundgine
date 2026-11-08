package com.foundgine.providers.gateway.http;

import java.math.BigDecimal;
import java.math.BigInteger;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** Shared strict helpers for turning parsed JSON trees into the gateway DTOs and bounded filter/field values. */
final class GatewayJsonValues {
    private GatewayJsonValues() { }

    /** Normalizes a parsed JSON value into a plain Java value (Long, BigDecimal, String, Boolean, List, Map, null). */
    static Object normalize(Object value, int depth, int maxDepth, String what) {
        if (depth > maxDepth) throw new IllegalArgumentException("JSON value depth exceeded.");
        if (value == null || value instanceof String || value instanceof Boolean || value instanceof Long) return value;
        if (value instanceof BigDecimal) return value;
        if (value instanceof BigInteger bi) return new BigDecimal(bi);
        if (value instanceof List<?> list) {
            var out = new ArrayList<Object>(list.size());
            for (Object item : list) out.add(normalize(item, depth + 1, maxDepth, what));
            return Collections.unmodifiableList(out);
        }
        if (value instanceof Map<?, ?> map) {
            var out = new LinkedHashMap<String, Object>();
            for (var e : map.entrySet()) out.put((String) e.getKey(), normalize(e.getValue(), depth + 1, maxDepth, what));
            return Collections.unmodifiableMap(out);
        }
        throw new IllegalArgumentException("Unsupported JSON " + what + " value.");
    }

    static Map<String, Object> object(Object value, String name) {
        if (!(value instanceof Map<?, ?> m)) throw new IllegalArgumentException(name + " must be a JSON object.");
        var out = new LinkedHashMap<String, Object>();
        for (var e : m.entrySet()) out.put((String) e.getKey(), e.getValue());
        return out;
    }

    static void allowOnly(Map<String, Object> map, String name, Set<String> allowed) {
        for (String key : map.keySet())
            if (!allowed.contains(key)) throw new IllegalArgumentException("Unmapped member '" + key + "' in " + name + ".");
    }

    static String string(Map<String, Object> map, String key) {
        Object v = map.get(key);
        if (v == null) return null;
        if (v instanceof String s) return s;
        throw new IllegalArgumentException("'" + key + "' must be a string.");
    }

    static Integer integer(Map<String, Object> map, String key) {
        Object v = map.get(key);
        if (v == null) return null;
        if (v instanceof Long l && l >= Integer.MIN_VALUE && l <= Integer.MAX_VALUE) return (int) l.longValue();
        throw new IllegalArgumentException("'" + key + "' must be a 32-bit integer.");
    }

    /** Reads an unsigned 64-bit JSON integer as its bit pattern; null when absent. */
    static Long unsigned(Object v, String name) {
        if (v == null) return null;
        if (v instanceof Long l && l >= 0) return l;
        if (v instanceof BigInteger b && b.signum() >= 0 && b.bitLength() <= 64) return b.longValue();
        throw new IllegalArgumentException("'" + name + "' must be an unsigned 64-bit integer.");
    }

    static List<Object> array(Map<String, Object> map, String key) {
        Object v = map.get(key);
        if (v == null) return null;
        if (v instanceof List<?> l) return new ArrayList<>(l);
        throw new IllegalArgumentException("'" + key + "' must be an array.");
    }

    static <T extends Enum<T>> T parseEnum(Class<T> type, String value, String name) {
        if (value != null) for (T t : type.getEnumConstants()) {
            if (t.name().equalsIgnoreCase(value.trim())) return t;
        }
        throw new IllegalArgumentException("Invalid " + name + ".");
    }

    static String required(String value, String name) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException(name + " is required.");
        return value;
    }
}
