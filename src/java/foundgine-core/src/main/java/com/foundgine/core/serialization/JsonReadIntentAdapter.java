package com.foundgine.core.serialization;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
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
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Translates a deliberately small JSON representation into Foundgine's
 * provider-neutral {@link ReadIntent}. It performs no semantic resolution,
 * authorization, planning, or provider work.
 *
 * <p>This walks the parsed Jackson {@link JsonNode} tree directly and
 * converts it into the domain read-intent types — {@link #rejectUnknown}
 * performs an unknown-property check applied per object node against
 * that node's known property names, rejecting untrusted JSON that contains
 * unexpected fields.
 */
public final class JsonReadIntentAdapter {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static final Set<String> INTENT_PROPERTIES =
            Set.of("rootEntity", "selections", "filter", "order", "limit", "offset", "after");
    private static final Set<String> SELECTION_PROPERTIES = Set.of("field", "relationship", "children");
    private static final Set<String> FILTER_PROPERTIES =
            Set.of("kind", "field", "operator", "value", "relationship", "quantifier", "predicate", "expressions");
    private static final Set<String> ORDER_PROPERTIES = Set.of("field", "direction", "relationshipPath", "aggregate");

    private final JsonReadIntentAdapterOptions limits;

    public JsonReadIntentAdapter() {
        this(null);
    }

    public JsonReadIntentAdapter(JsonReadIntentAdapterOptions limits) {
        this.limits = limits == null ? new JsonReadIntentAdapterOptions() : limits;
        validateLimits(this.limits);
    }

    public ReadIntent parse(String json) {
        if (json == null || json.isBlank())
            throw new IllegalArgumentException("json must not be null or blank.");

        JsonNode root;
        try {
            root = MAPPER.readTree(json);
        } catch (JsonProcessingException e) {
            throw invalid("Invalid JSON read intent: " + e.getMessage(), e);
        }
        if (root == null || root.isNull() || root.isMissingNode())
            throw invalid("JSON document is empty.");

        return toIntent(asObject(root, "root document"));
    }

    private ReadIntent toIntent(ObjectNode dto) {
        rejectUnknown(dto, INTENT_PROPERTIES);

        String rootEntity = text(dto, "rootEntity");
        if (rootEntity == null || rootEntity.isBlank())
            throw invalid("'rootEntity' is required.");

        JsonNode selectionsNode = dto.get("selections");
        if (selectionsNode == null || !selectionsNode.isArray() || selectionsNode.isEmpty())
            throw invalid("'selections' must contain at least one selection.");

        if (countSelections((ArrayNode) selectionsNode, 1) > limits.maxSelections())
            throw invalid("Selection count exceeds the configured maximum of " + limits.maxSelections() + ".");

        List<ReadSelection> selections = new ArrayList<>();
        for (JsonNode child : selectionsNode)
            selections.add(toSelection(asObject(child, "selection"), 1));

        JsonNode filterNode = dto.get("filter");
        ReadFilter filter = filterNode == null || filterNode.isNull() ? null : toFilter(asObject(filterNode, "filter"));

        List<ReadOrder> order = null;
        JsonNode orderNode = dto.get("order");
        if (orderNode != null && orderNode.isArray()) {
            order = new ArrayList<>();
            for (JsonNode item : orderNode)
                order.add(toOrder(asObject(item, "order")));
        }

        Integer limit = intOrNull(dto, "limit");
        Integer offset = intOrNull(dto, "offset");
        if ((limit != null && limit < 0) || (offset != null && offset < 0))
            throw invalidArgument("'limit' and 'offset' cannot be negative.");

        String after = text(dto, "after");

        return new ReadIntent(rootEntity, selections, filter, order, limit, offset, after, null);
    }

    private ReadSelection toSelection(ObjectNode dto, int depth) {
        if (depth > limits.maxSelectionDepth())
            throw invalid("Selection depth exceeds the configured maximum of " + limits.maxSelectionDepth() + ".");
        rejectUnknown(dto, SELECTION_PROPERTIES);

        String field = text(dto, "field");
        String relationship = text(dto, "relationship");
        boolean hasField = field != null && !field.isBlank();
        boolean hasRelationship = relationship != null && !relationship.isBlank();
        if (hasField == hasRelationship)
            throw invalidArgument("Each selection must specify exactly one of 'field' or 'relationship'.");

        List<ReadSelection> children = List.of();
        JsonNode childrenNode = dto.get("children");
        if (childrenNode != null && childrenNode.isArray()) {
            List<ReadSelection> collected = new ArrayList<>();
            for (JsonNode child : childrenNode)
                collected.add(toSelection(asObject(child, "selection child"), depth + 1));
            children = collected;
        }

        return new ReadSelection(hasField ? field : null, hasRelationship ? relationship : null, children);
    }

    private ReadFilter toFilter(ObjectNode dto) {
        int[] nodes = {0};
        return toFilter(dto, 1, nodes);
    }

    private ReadFilter toFilter(ObjectNode dto, int depth, int[] nodes) {
        if (depth > limits.maxFilterDepth())
            throw invalid("Filter depth exceeds the configured maximum of " + limits.maxFilterDepth() + ".");
        if (++nodes[0] > limits.maxFilterNodes())
            throw invalid("Filter node count exceeds the configured maximum of " + limits.maxFilterNodes() + ".");
        rejectUnknown(dto, FILTER_PROPERTIES);

        String kind = text(dto, "kind");
        if (kind == null)
            throw invalid("Every filter requires a 'kind'.");

        return switch (kind.trim().toLowerCase(java.util.Locale.ROOT)) {
            case "field" -> new ReadFieldFilter(
                    required(text(dto, "field"), "filter.field"),
                    parseEnum(SemanticFilterOperator.class, text(dto, "operator"), "Field filters require 'operator'."),
                    toValue(dto.get("value"), 0));

            case "relationship" -> new ReadRelationshipFilter(
                    required(text(dto, "relationship"), "filter.relationship"),
                    parseEnum(SemanticRelationshipQuantifier.class, text(dto, "quantifier"),
                            "Relationship filters require 'quantifier'."),
                    dto.get("predicate") == null || dto.get("predicate").isNull()
                            ? throwInvalid("Relationship filters require 'predicate'.")
                            : toFilter(asObject(dto.get("predicate"), "filter.predicate"), depth + 1, nodes));

            case "and" -> new ReadAndFilter(requireExpressions(dto, depth, nodes, "AND"));

            case "or" -> new ReadOrFilter(requireExpressions(dto, depth, nodes, "OR"));

            default -> throw invalidArgument("Unsupported filter kind '" + kind + "'.");
        };
    }

    private List<ReadFilter> requireExpressions(ObjectNode dto, int depth, int[] nodes, String kindLabel) {
        JsonNode expressionsNode = dto.get("expressions");
        if (expressionsNode == null || !expressionsNode.isArray() || expressionsNode.isEmpty())
            throw invalidArgument(kindLabel + " filters require at least one expression.");

        List<ReadFilter> result = new ArrayList<>();
        for (JsonNode expression : expressionsNode)
            result.add(toFilter(asObject(expression, "filter expression"), depth + 1, nodes));
        return result;
    }

    private Object toValue(JsonNode value, int depth) {
        return value == null || value.isNull() ? null : normalize(value, depth);
    }

    private Object normalize(JsonNode value, int depth) {
        if (depth > limits.maxJsonValueDepth())
            throw invalidArgument("JSON value depth exceeds the configured maximum of " + limits.maxJsonValueDepth() + ".");

        switch (value.getNodeType()) {
            case NULL:
                return null;
            case BOOLEAN:
                return value.booleanValue();
            case STRING:
                return value.textValue();
            case NUMBER:
                if (value.isIntegralNumber() && value.canConvertToLong())
                    return value.longValue();
                return value.decimalValue();
            case ARRAY: {
                List<Object> items = new ArrayList<>();
                for (JsonNode item : value)
                    items.add(normalize(item, depth + 1));
                return items;
            }
            case OBJECT: {
                Map<String, Object> map = new LinkedHashMap<>();
                Iterator<Map.Entry<String, JsonNode>> fields = value.fields();
                while (fields.hasNext()) {
                    Map.Entry<String, JsonNode> entry = fields.next();
                    map.put(entry.getKey(), normalize(entry.getValue(), depth + 1));
                }
                return map;
            }
            default:
                throw invalid("Unsupported JSON value kind '" + value.getNodeType() + "'.");
        }
    }

    private ReadOrder toOrder(ObjectNode dto) {
        rejectUnknown(dto, ORDER_PROPERTIES);

        List<String> relationshipPath = null;
        JsonNode pathNode = dto.get("relationshipPath");
        if (pathNode != null && pathNode.isArray()) {
            relationshipPath = new ArrayList<>();
            for (JsonNode item : pathNode)
                relationshipPath.add(item.textValue());
        }

        String aggregateText = text(dto, "aggregate");
        SemanticOrderAggregate aggregate = aggregateText == null
                ? SemanticOrderAggregate.NONE
                : parseEnum(SemanticOrderAggregate.class, aggregateText, "Invalid 'aggregate' value.");

        return new ReadOrder(
                required(text(dto, "field"), "order.field"),
                parseEnum(SemanticSortDirection.class, text(dto, "direction"), "Order entries require 'direction'."),
                relationshipPath,
                aggregate);
    }

    private int countSelections(ArrayNode selections, int depth) {
        if (depth > limits.maxSelectionDepth())
            throw invalid("Selection depth exceeds the configured maximum of " + limits.maxSelectionDepth() + ".");

        int count = 0;
        for (JsonNode selectionNode : selections) {
            if (++count > limits.maxSelections())
                return count;

            JsonNode childrenNode = selectionNode.get("children");
            if (childrenNode != null && childrenNode.isArray()) {
                count += countSelections((ArrayNode) childrenNode, depth + 1);
                if (count > limits.maxSelections())
                    return count;
            }
        }

        return count;
    }

    private void rejectUnknown(ObjectNode dto, Set<String> known) {
        if (!limits.rejectUnknownProperties())
            return;
        Iterator<String> names = dto.fieldNames();
        while (names.hasNext()) {
            String name = names.next();
            if (!known.contains(name))
                throw invalid("Unrecognized property '" + name + "'.");
        }
    }

    private static void validateLimits(JsonReadIntentAdapterOptions limits) {
        if (limits.maxSelectionDepth() < 1 || limits.maxSelections() < 1
                || limits.maxFilterDepth() < 1 || limits.maxFilterNodes() < 1
                || limits.maxJsonValueDepth() < 1)
            throw new IllegalArgumentException("All parser limits must be positive.");
    }

    private static ObjectNode asObject(JsonNode node, String context) {
        if (node == null || !node.isObject())
            throw invalid("'" + context + "' must be a JSON object.");
        return (ObjectNode) node;
    }

    private static String text(ObjectNode dto, String field) {
        JsonNode node = dto.get(field);
        return node == null || node.isNull() ? null : node.asText();
    }

    private static Integer intOrNull(ObjectNode dto, String field) {
        JsonNode node = dto.get(field);
        return node == null || node.isNull() ? null : node.asInt();
    }

    private static String required(String value, String name) {
        if (value == null || value.isBlank())
            throw invalid("'" + name + "' is required.");
        return value;
    }

    private static <E extends Enum<E>> E parseEnum(Class<E> type, String value, String missingMessage) {
        if (value == null || value.isBlank())
            throw invalid(missingMessage);
        try {
            return Enum.valueOf(type, value.trim().toUpperCase(java.util.Locale.ROOT));
        } catch (IllegalArgumentException e) {
            throw invalidArgument("Unsupported value '" + value + "' for " + type.getSimpleName() + ".");
        }
    }

    private static IllegalStateException invalid(String message) {
        return new IllegalStateException(message);
    }

    /**
     * Thrown for malformed-shape violations of the read intent itself (the
     * caller's JSON does not describe a structurally valid intent) — mirrors
     * this port's convention of using {@link IllegalArgumentException} for
     * argument-shape validation, matching the exception domain records such
     * as {@link com.foundgine.core.semantic.intent.ReadSelection} already
     * throw for the same class of violation. This is distinct from
     * {@link #invalid(String)}, which is reserved for untrusted-transport
     * security-boundary rejections (e.g. unrecognized/security-authority
     * properties) and resource-limit enforcement, both ported from this
     * codebase's {@code InvalidOperationException} → {@code IllegalStateException}
     * mapping.
     */
    private static IllegalArgumentException invalidArgument(String message) {
        return new IllegalArgumentException(message);
    }

    private static IllegalStateException invalid(String message, Throwable inner) {
        return new IllegalStateException(message, inner);
    }

    private static ReadFilter throwInvalid(String message) {
        throw invalid(message);
    }
}