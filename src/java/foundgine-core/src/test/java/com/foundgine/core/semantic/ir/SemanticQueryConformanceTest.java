package com.foundgine.core.semantic.ir;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import java.io.File;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import java.util.stream.StreamSupport;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Cross-language conformance for {@link SemanticQuery}: asserts src/conformance/semantic-query-vectors.json, the same
 * file the C# {@code SemanticQueryConformanceTests} asserts. Both builders must produce the same operation (rendered
 * canonically by names) or the same error message. Override the directory with -Dfoundgine.conformance.dir=...
 */
class SemanticQueryConformanceTest {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static JsonNode root() throws Exception {
        String dir = System.getProperty("foundgine.conformance.dir");
        File f = new File(dir != null ? dir : "../../conformance", "semantic-query-vectors.json");
        if (!f.isFile()) throw new IllegalStateException("Conformance vectors not found: " + f.getAbsolutePath());
        return MAPPER.readTree(Files.readAllBytes(f.toPath()));
    }

    static Stream<JsonNode> cases() throws Exception {
        List<JsonNode> all = new ArrayList<>();
        root().get("cases").forEach(all::add);
        return all.stream();
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("cases")
    void builderMatchesTheReferenceVector(JsonNode vector) throws Exception {
        MetadataRegistry model = buildModel(root().get("model"));
        if (vector.has("error")) {
            var ex = assertThrows(IllegalArgumentException.class, () -> run(model, vector));
            assertEquals(vector.get("error").asText(), ex.getMessage());
            return;
        }
        assertEquals(vector.get("rendered").asText(), render(model, run(model, vector).root()));
    }

    @Test
    void vectorFileDeclaresTheSpec() throws Exception {
        assertEquals("semantic-query-v1", root().get("spec").asText());
    }

    private static SemanticOperation run(MetadataRegistry model, JsonNode vector) {
        var query = SemanticQuery.from(model, vector.get("entity").asText());
        apply(model, query, vector.get("steps"));
        return query.build();
    }

    private static void apply(MetadataRegistry model, SemanticQuery query, JsonNode steps) {
        for (JsonNode step : steps) {
            switch (step.get("op").asText()) {
                case "select" -> {
                    List<String> names = new ArrayList<>();
                    step.get("names").forEach(n -> names.add(n.asText()));
                    query.select(names);
                }
                case "whereEq" -> query.whereEq(step.get("field").asText(), value(step.get("value")));
                case "whereNeq" -> query.whereNeq(step.get("field").asText(), value(step.get("value")));
                case "whereIn" -> {
                    List<Object> values = new ArrayList<>();
                    step.get("values").forEach(v -> values.add(value(v)));
                    query.whereIn(step.get("field").asText(), values.toArray());
                }
                case "orderBy" -> query.orderBy(
                        step.get("field").asText(),
                        step.get("dir").asText().equals("desc") ? SemanticSortDirection.DESC : SemanticSortDirection.ASC);
                case "take" -> query.take(step.get("n").asInt());
                case "skip" -> query.skip(step.get("n").asInt());
                case "include" -> {
                    String relName = step.get("relationship").asText();
                    var rel = StreamSupport.stream(model.relationships().spliterator(), false)
                            .filter(r -> r.name().equals(relName)).findFirst().orElseThrow();
                    query.include(rel.id(), child -> apply(model, child, step.get("steps")));
                }
                default -> throw new IllegalStateException("Unknown step " + step);
            }
        }
    }

    private static Object value(JsonNode n) {
        return n.isTextual() ? n.asText() : (Object) n.asInt();
    }

    // ---- canonical rendering (spec semantic-query-v1, see conformance/README.md) ----

    private static String render(MetadataRegistry model, SemanticReadNode node) {
        Map<FieldId, String> names = new HashMap<>();
        for (EntityMetadata e : model.entities()) e.effectiveFields().forEach(f -> names.put(f.id(), f.name()));
        var sb = new StringBuilder();
        sb.append('#').append(node.id()).append(' ').append(model.get(node.entityId()).name()).append('(')
                .append(node.fields().stream().map(names::get).collect(Collectors.joining(", "))).append(')');
        if (node.viaRelationship() != null) sb.append(" via ").append(model.get(node.viaRelationship()).name());
        var options = node.queryOptions();
        if (options != null && options.filter() != null) sb.append(" where ").append(filter(options.filter(), names));
        if (options != null && !options.order().isEmpty()) {
            sb.append(" order ").append(options.order().stream()
                    .map(o -> names.get(o.field()) + " " + (o.direction() == SemanticSortDirection.DESC ? "desc" : "asc"))
                    .collect(Collectors.joining(", ")));
        }
        if (options != null && options.limit() != null) sb.append(" limit ").append(options.limit());
        if (options != null && options.offset() != null) sb.append(" offset ").append(options.offset());
        if (!node.children().isEmpty()) {
            sb.append(" [").append(node.children().stream().map(c -> render(model, c))
                    .collect(Collectors.joining("; "))).append(']');
        }
        return sb.toString();
    }

    private static String filter(SemanticFilterExpression expression, Map<FieldId, String> names) {
        if (expression instanceof SemanticAndFilter and) {
            return "(" + and.expressions().stream().map(e -> filter(e, names)).collect(Collectors.joining(" AND "))
                    + ")";
        }
        var f = (SemanticFieldFilter) expression;
        String field = names.get(f.field());
        return switch (f.operator()) {
            case EQ -> field + " eq " + scalar(f.value());
            case NEQ -> field + " neq " + scalar(f.value());
            case IN -> field + " in [" + join((Iterable<?>) f.value()) + "]";
            default -> throw new IllegalStateException(f.operator().toString());
        };
    }

    private static String join(Iterable<?> values) {
        var out = new ArrayList<String>();
        values.forEach(v -> out.add(scalar(v)));
        return String.join(", ", out);
    }

    private static String scalar(Object v) {
        return v instanceof String s ? "\"" + s + "\"" : String.valueOf(v);
    }

    private static MetadataRegistry buildModel(JsonNode model) {
        var registry = new MetadataRegistry();
        model.get("entities").fields().forEachRemaining(entity -> {
            List<FieldMetadata> fields = new ArrayList<>();
            entity.getValue().forEach(f -> fields.add(new FieldMetadata(
                    FieldId.create(entity.getKey(), f.get(0).asText()), f.get(0).asText(),
                    f.get(1).asText().equals("int") ? Integer.class : String.class)));
            registry.register(new EntityMetadata(
                    EntityId.create(entity.getKey()), entity.getKey(), List.of(), null, fields, null, null, false,
                    null, null));
        });
        model.get("relationships").forEach(r -> {
            String source = r.get("source").asText();
            String target = r.get("target").asText();
            String name = r.get("name").asText();
            registry.register(new RelationshipMetadata(
                    RelationshipId.create(source, name), EntityId.create(source), EntityId.create(target), name,
                    new ColumnReference(EntityId.create(source), ColumnId.create(source, "Id")),
                    new ColumnReference(EntityId.create(target), ColumnId.create(target, source + "Id"))));
        });
        return registry;
    }
}
