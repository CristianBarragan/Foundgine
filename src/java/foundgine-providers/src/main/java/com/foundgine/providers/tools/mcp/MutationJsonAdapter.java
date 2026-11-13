package com.foundgine.providers.tools.mcp;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.UnsignedLongJson;
import com.foundgine.core.semantic.mutation.SemanticMutationBuilder;
import com.foundgine.core.semantic.mutation.SemanticMutationField;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.runtime.SemanticMutationRequest;
import java.math.BigInteger;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Parses the MCP mutation JSON into a {@link SemanticMutationRequest} (port of the C# {@code MutationJsonAdapter}). Shape:
 * {@code {"operations":[{"entity":<id>,"kind":"create|update|delete|upsert","fields":{"<fieldId>":value},"filter":{...},
 * "returnFields":[ids],"conflictFields":[ids]}]}}; a filter is {@code {"and":[..]}}, {@code {"or":[..]}} or
 * {@code {"field":<id>,"operator":"eq|neq|in|gt|gte|lt|lte","value":..}}. Ids are unsigned 64-bit integers. The result never
 * carries security: the tools attach the host's context. Malformed input is an {@link IllegalArgumentException}.
 */
public final class MutationJsonAdapter {
    private MutationJsonAdapter() {}

    public static SemanticMutationRequest parse(String json) {
        if (json == null || json.isBlank()) throw new IllegalArgumentException("Mutation JSON is required.");
        JsonNode root;
        try {
            root = FoundgineMcpJson.READER.readTree(json);
        } catch (JsonProcessingException e) {
            throw new IllegalArgumentException("Mutation JSON is not valid JSON.", e);
        }
        var operations = root == null ? null : root.get("operations");
        if (operations == null || !operations.isArray() || operations.isEmpty())
            throw new IllegalArgumentException("'operations' must contain at least one mutation operation.");
        var list = new ArrayList<SemanticMutationOperation>();
        for (var op : operations) list.add(parseOperation(op));
        return new SemanticMutationRequest(new SemanticMutationOperationGraph(list));
    }

    private static SemanticMutationOperation parseOperation(JsonNode op) {
        if (op == null || !op.isObject()) throw new IllegalArgumentException("Each mutation operation must be a JSON object.");
        var entity = new EntityId(readUnsigned(op, "entity"));
        var kind = parseKind(text(op, "kind"));
        var fields = new ArrayList<SemanticMutationField>();
        var fieldObject = op.get("fields");
        if (fieldObject != null && fieldObject.isObject()) {
            for (var it = fieldObject.fields(); it.hasNext(); ) {
                var p = it.next();
                fields.add(new SemanticMutationField(new FieldId(parseId(p.getKey(), "fields key")), toValue(p.getValue())));
            }
        }
        var returns = ids(op, "returnFields");
        var conflicts = ids(op, "conflictFields");
        var filterJson = op.get("filter");
        SemanticFilterExpression filter = filterJson == null || filterJson.isNull() ? null : parseFilter(filterJson);
        return switch (kind) {
            case CREATE -> SemanticMutationBuilder.create(entity, fields, returns);
            case UPDATE -> SemanticMutationBuilder.update(entity, fields, filter, returns);
            case DELETE -> {
                if (filter == null) throw new IllegalArgumentException("Delete requires a filter.");
                yield SemanticMutationBuilder.delete(entity, filter, returns);
            }
            case UPSERT -> SemanticMutationBuilder.upsert(entity, fields, conflicts, returns);
        };
    }

    private static SemanticFilterExpression parseFilter(JsonNode json) {
        if (json == null || !json.isObject()) throw new IllegalArgumentException("A filter must be a JSON object.");
        if (json.has("and")) return new SemanticAndFilter(children(json.get("and")));
        if (json.has("or")) return new SemanticOrFilter(children(json.get("or")));
        var operator = text(json, "operator");
        SemanticFilterOperator parsed;
        try {
            parsed = SemanticFilterOperator.valueOf(operator.toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("Unknown filter operator '" + operator + "'.", e);
        }
        return new SemanticFieldFilter(new FieldId(readUnsigned(json, "field")), parsed,
                json.has("value") ? toValue(json.get("value")) : null);
    }

    private static List<SemanticFilterExpression> children(JsonNode array) {
        if (array == null || !array.isArray()) throw new IllegalArgumentException("'and'/'or' must be arrays of filters.");
        var out = new ArrayList<SemanticFilterExpression>();
        for (var child : array) out.add(parseFilter(child));
        return out;
    }

    private static SemanticMutationKind parseKind(String kind) {
        try {
            return SemanticMutationKind.valueOf(kind.toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("Unknown mutation kind '" + kind + "'.", e);
        }
    }

    private static List<FieldId> ids(JsonNode op, String property) {
        var values = op.get(property);
        var out = new ArrayList<FieldId>();
        if (values != null && values.isArray()) for (var v : values) out.add(new FieldId(unsigned(v, property)));
        return out;
    }

    private static long readUnsigned(JsonNode obj, String property) { return unsigned(obj.get(property), property); }

    /** An unsigned 64-bit JSON integer (C# {@code GetUInt64}); strings, fractions and negatives are rejected. */
    private static long unsigned(JsonNode node, String name) {
        if (node == null || !node.isIntegralNumber()) throw new IllegalArgumentException("'" + name + "' must be an unsigned 64-bit integer.");
        BigInteger value = node.bigIntegerValue();
        if (value.signum() < 0 || value.bitLength() > 64) throw new IllegalArgumentException("'" + name + "' must be an unsigned 64-bit integer.");
        return UnsignedLongJson.parseUnsigned(value.toString());
    }

    private static long parseId(String text, String name) {
        try {
            return UnsignedLongJson.parseUnsigned(text);
        } catch (NumberFormatException e) {
            throw new IllegalArgumentException("'" + name + "' must be an unsigned 64-bit integer.", e);
        }
    }

    private static String text(JsonNode obj, String property) {
        var v = obj.get(property);
        if (v == null || !v.isTextual() || v.asText().isBlank()) throw new IllegalArgumentException("'" + property + "' is required.");
        return v.asText();
    }

    /** Mirrors the C# value mapping: null, bool, long, decimal, string; arrays and objects stay structured. */
    private static Object toValue(JsonNode value) {
        if (value == null || value.isNull()) return null;
        if (value.isBoolean()) return value.booleanValue();
        if (value.isIntegralNumber() && value.canConvertToLong()) return value.longValue();
        if (value.isNumber()) return value.decimalValue();
        if (value.isTextual()) return value.textValue();
        return FoundgineMcpJson.READER.convertValue(value, Object.class);
    }
}
