package com.foundgine.extensions.graphql;

import graphql.language.ArrayValue;
import graphql.language.BooleanValue;
import graphql.language.EnumValue;
import graphql.language.FloatValue;
import graphql.language.IntValue;
import graphql.language.ListType;
import graphql.language.NonNullType;
import graphql.language.NullValue;
import graphql.language.ObjectField;
import graphql.language.ObjectValue;
import graphql.language.StringValue;
import graphql.language.Type;
import graphql.language.TypeName;
import graphql.language.Value;
import graphql.language.VariableDefinition;
import graphql.language.VariableReference;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Iterator;
import java.util.List;
import java.util.Map;

/**
 * Performs GraphQL variable input coercion at the adapter boundary. Foundgine core receives ordinary Java values and
 * never sees GraphQL variable/type syntax. Port of {@code GraphQLVariableCoercer}.
 *
 * <p>Runtime variable values are plain Java values (as produced by {@code FoundgineJson.parse} or Jackson binding to
 * {@code Map}): {@code Map}, {@code List}/arrays, {@code String}, {@code Number}, {@code Boolean}, {@code null}. Objects
 * become case-insensitive maps and lists become {@code List<Object>} (the C# adapter yields {@code object[]}).
 */
public final class GraphQLVariableCoercer {
    private GraphQLVariableCoercer() { }

    public static Object resolve(
            String name, Map<String, Object> variables, Map<String, VariableDefinition> definitions) {
        var definition = definitions.get(name);
        if (definition == null)
            throw new IllegalStateException("GraphQL variable '$" + name + "' is not defined by the operation.");

        boolean supplied = variables != null && variables.containsKey(name);
        Object value;
        if (supplied) value = normalize(variables.get(name));
        else if (definition.getDefaultValue() != null) value = fromSyntax(definition.getDefaultValue());
        else value = null;

        if (!supplied && definition.getDefaultValue() == null && definition.getType() instanceof NonNullType)
            throw new IllegalStateException("GraphQL variable '$" + name
                    + "' requires a runtime variable-value dictionary; it was not supplied, and its non-null type requires a value.");

        return coerce(value, definition.getType(), "$" + name);
    }

    public static Object resolveValue(
            Value<?> node, Map<String, Object> variables, Map<String, VariableDefinition> definitions) {
        return resolveSyntax(node, variables, definitions);
    }

    /**
     * GraphQL variable values may contain keys that are not declared by the selected operation. Those extra runtime
     * values are ignored; validation happens when a declared variable is resolved/coerced.
     */
    public static void validateSuppliedVariables(
            Map<String, Object> variables, Map<String, VariableDefinition> definitions) { }

    private static Object coerce(Object value, Type<?> type, String path) {
        if (type instanceof NonNullType nonNull) {
            if (value == null)
                throw new IllegalStateException(
                        "GraphQL variable value at " + path + " cannot be null because its type is non-null.");
            return coerce(value, nonNull.getType(), path);
        }
        if (value == null) return null;

        if (type instanceof ListType listType) {
            if (value instanceof Map<?, ?>)
                throw new IllegalStateException("GraphQL variable value at " + path + " must be a list.");
            if (value instanceof String || !isSequence(value)) {
                // GraphQL singleton-to-list input coercion
                var single = new ArrayList<Object>();
                single.add(coerce(value, listType.getType(), path + "[0]"));
                return Collections.unmodifiableList(single);
            }
            var result = new ArrayList<Object>();
            int index = 0;
            for (var item : items(value)) result.add(coerce(item, listType.getType(), path + "[" + index++ + "]"));
            return Collections.unmodifiableList(result);
        }

        return coerceNamed(value, ((TypeName) type).getName(), path);
    }

    private static Object coerceNamed(Object value, String typeName, String path) {
        switch (typeName) {
            case "Int":
                return coerceInt(value, path);
            case "Float":
                return coerceFloat(value, path);
            case "String":
                if (value instanceof String) return value;
                throw typeError(path, "String", value);
            case "Boolean":
                if (value instanceof Boolean) return value;
                throw typeError(path, "Boolean", value);
            case "ID":
                if (value instanceof String) return value;
                if (value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long)
                    return value.toString();
                throw typeError(path, "ID", value);
            default:
                // Without the application schema the adapter cannot distinguish an input object from an enum/custom
                // scalar by name. It can, however, enforce the GraphQL input-object shape when the runtime value is
                // object-like and preserve custom scalar values unchanged.
                if (value instanceof Map<?, ?> map) return caseInsensitive(map);
                return value;
        }
    }

    private static Object coerceInt(Object value, String path) {
        if (value instanceof Byte || value instanceof Short || value instanceof Integer)
            return ((Number) value).intValue();
        if (value instanceof Long l && l >= Integer.MIN_VALUE && l <= Integer.MAX_VALUE) return l.intValue();
        throw typeError(path, "Int", value);
    }

    private static Object coerceFloat(Object value, String path) {
        if (value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long
                || value instanceof Float || value instanceof Double || value instanceof BigDecimal)
            return ((Number) value).doubleValue();
        throw typeError(path, "Float", value);
    }

    private static Object resolveSyntax(
            Value<?> node, Map<String, Object> variables, Map<String, VariableDefinition> definitions) {
        if (node instanceof VariableReference v) return resolve(v.getName(), variables, definitions);
        if (node instanceof StringValue s) return s.getValue();
        if (node instanceof IntValue i) {
            var big = i.getValue();
            return big.bitLength() < 64 ? (Object) big.longValue() : big.toString(); // beyond long: raw text, as C#
        }
        if (node instanceof FloatValue f) return f.getValue().doubleValue();
        if (node instanceof BooleanValue b) return b.isValue();
        if (node instanceof NullValue) return null;
        if (node instanceof EnumValue e) return e.getName();
        if (node instanceof ArrayValue list) {
            var result = new ArrayList<Object>();
            for (var item : list.getValues()) result.add(resolveSyntax(item, variables, definitions));
            return Collections.unmodifiableList(result);
        }
        if (node instanceof ObjectValue obj) {
            var result = new CaseInsensitiveOrderedMap();
            for (ObjectField field : obj.getObjectFields()) {
                if (result.containsKey(field.getName()))
                    throw new IllegalArgumentException(
                            "An item with the same key has already been added. Key: " + field.getName());
                result.put(field.getName(), resolveSyntax(field.getValue(), variables, definitions));
            }
            return result.freeze();
        }
        return String.valueOf(node);
    }

    private static Object fromSyntax(Value<?> node) {
        return resolveSyntax(node, null, Map.of());
    }

    private static Object normalize(Object value) {
        if (value == null) return null;
        if (value instanceof Map<?, ?> map) return caseInsensitive(map);
        if (!(value instanceof String) && isSequence(value)) {
            var result = new ArrayList<Object>();
            for (var item : items(value)) result.add(normalize(item));
            return Collections.unmodifiableList(result);
        }
        return value;
    }

    private static Map<String, Object> caseInsensitive(Map<?, ?> source) {
        var result = new CaseInsensitiveOrderedMap();
        for (var entry : source.entrySet()) {
            var key = String.valueOf(entry.getKey());
            if (result.containsKey(key))
                throw new IllegalArgumentException("An item with the same key has already been added. Key: " + key);
            result.put(key, normalize(entry.getValue()));
        }
        return result.freeze();
    }

    private static boolean isSequence(Object value) {
        return value instanceof Iterable<?> || value.getClass().isArray();
    }

    private static Iterable<?> items(Object value) {
        if (value instanceof Iterable<?> iterable) return iterable;
        int length = java.lang.reflect.Array.getLength(value);
        return () -> new Iterator<Object>() {
            private int i;

            @Override
            public boolean hasNext() {
                return i < length;
            }

            @Override
            public Object next() {
                return java.lang.reflect.Array.get(value, i++);
            }
        };
    }

    private static IllegalStateException typeError(String path, String expected, Object value) {
        return new IllegalStateException("GraphQL variable value at " + path + " must be " + expected
                + "; received " + value.getClass().getSimpleName() + ".");
    }
}
