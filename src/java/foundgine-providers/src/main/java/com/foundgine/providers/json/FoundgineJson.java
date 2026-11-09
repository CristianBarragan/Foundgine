package com.foundgine.providers.json;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Minimal, reflection-free JSON writer/reader used for the gateway journal and outbox header wire formats.
 * Objects parse to {@code LinkedHashMap<String,Object>}, arrays to {@code List<Object>}, numbers to {@code Long}
 * (integral, fits a signed long), {@code BigInteger} (integral, larger) or {@code BigDecimal} (fractional or
 * exponent form), plus {@code String}, {@code Boolean} and {@code null}. Numbers are never narrowed to double.
 */
public final class FoundgineJson {
    private FoundgineJson() { }

    public static void writeString(StringBuilder sb, String value) {
        sb.append('"');
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            switch (c) {
                case '"' -> sb.append("\\\"");
                case '\\' -> sb.append("\\\\");
                case '\n' -> sb.append("\\n");
                case '\r' -> sb.append("\\r");
                case '\t' -> sb.append("\\t");
                case '\b' -> sb.append("\\b");
                case '\f' -> sb.append("\\f");
                default -> {
                    if (c < 0x20) sb.append(String.format("\\u%04x", (int) c)); else sb.append(c);
                }
            }
        }
        sb.append('"');
    }

    public static void writeNullableString(StringBuilder sb, String value) {
        if (value == null) sb.append("null"); else writeString(sb, value);
    }

    public static void writeStringMap(StringBuilder sb, Map<String, String> map) {
        sb.append('{');
        boolean first = true;
        for (var e : map.entrySet()) {
            if (!first) sb.append(',');
            first = false;
            writeString(sb, e.getKey());
            sb.append(':');
            writeString(sb, e.getValue());
        }
        sb.append('}');
    }

    public static String stringMapToJson(Map<String, String> map) {
        var sb = new StringBuilder();
        writeStringMap(sb, map);
        return sb.toString();
    }

    /** Parses a JSON object whose values are all strings; any other value kind is rejected. */
    public static Map<String, String> parseStringMap(String json) {
        Object value = parse(json);
        if (!(value instanceof Map<?, ?> m)) throw new IllegalArgumentException("JSON object expected.");
        var result = new LinkedHashMap<String, String>();
        for (var e : m.entrySet()) {
            if (!(e.getValue() instanceof String s)) throw new IllegalArgumentException("String value expected for '" + e.getKey() + "'.");
            result.put((String) e.getKey(), s);
        }
        return result;
    }

    public static Object parse(String json) {
        if (json == null) throw new IllegalArgumentException("json is required");
        var p = new Parser(json);
        p.skipWs();
        Object v = p.value();
        p.skipWs();
        if (p.i != json.length()) throw p.error("Unexpected trailing content");
        return v;
    }

    private static final class Parser {
        final String s; int i;
        Parser(String s) { this.s = s; }
        IllegalArgumentException error(String m) { return new IllegalArgumentException(m + " at position " + i + "."); }
        void skipWs() { while (i < s.length() && Character.isWhitespace(s.charAt(i))) i++; }
        char peek() { if (i >= s.length()) throw error("Unexpected end of JSON"); return s.charAt(i); }
        void expect(char c) { if (peek() != c) throw error("Expected '" + c + "'"); i++; }

        Object value() {
            char c = peek();
            switch (c) {
                case '{': return object();
                case '[': return array();
                case '"': return string();
                case 't': literal("true"); return Boolean.TRUE;
                case 'f': literal("false"); return Boolean.FALSE;
                case 'n': literal("null"); return null;
                default: return number();
            }
        }
        void literal(String lit) {
            if (!s.startsWith(lit, i)) throw error("Invalid literal");
            i += lit.length();
        }
        Map<String, Object> object() {
            expect('{'); skipWs();
            var map = new LinkedHashMap<String, Object>();
            if (peek() == '}') { i++; return map; }
            while (true) {
                skipWs();
                String key = string();
                skipWs(); expect(':'); skipWs();
                if (map.containsKey(key)) throw error("Duplicate property '" + key + "'");
                map.put(key, value());
                skipWs();
                char c = peek();
                if (c == ',') { i++; continue; }
                if (c == '}') { i++; return map; }
                throw error("Expected ',' or '}'");
            }
        }
        List<Object> array() {
            expect('['); skipWs();
            var list = new ArrayList<Object>();
            if (peek() == ']') { i++; return list; }
            while (true) {
                skipWs();
                list.add(value());
                skipWs();
                char c = peek();
                if (c == ',') { i++; continue; }
                if (c == ']') { i++; return list; }
                throw error("Expected ',' or ']'");
            }
        }
        String string() {
            expect('"');
            var sb = new StringBuilder();
            while (true) {
                char c = peek(); i++;
                if (c == '"') return sb.toString();
                if (c != '\\') { sb.append(c); continue; }
                char e = peek(); i++;
                switch (e) {
                    case '"' -> sb.append('"');
                    case '\\' -> sb.append('\\');
                    case '/' -> sb.append('/');
                    case 'n' -> sb.append('\n');
                    case 'r' -> sb.append('\r');
                    case 't' -> sb.append('\t');
                    case 'b' -> sb.append('\b');
                    case 'f' -> sb.append('\f');
                    case 'u' -> {
                        if (i + 4 > s.length()) throw error("Invalid unicode escape");
                        sb.append((char) Integer.parseInt(s.substring(i, i + 4), 16));
                        i += 4;
                    }
                    default -> throw error("Invalid escape");
                }
            }
        }
        Object number() {
            int start = i;
            while (i < s.length() && "+-0123456789.eE".indexOf(s.charAt(i)) >= 0) i++;
            if (start == i) throw error("Unexpected character");
            String t = s.substring(start, i);
            try {
                if (t.indexOf('.') < 0 && t.indexOf('e') < 0 && t.indexOf('E') < 0) {
                    try { return Long.parseLong(t); } catch (NumberFormatException overflow) { return new java.math.BigInteger(t); }
                }
                return new java.math.BigDecimal(t);
            } catch (NumberFormatException ex) { throw error("Invalid number"); }
        }
    }
}
