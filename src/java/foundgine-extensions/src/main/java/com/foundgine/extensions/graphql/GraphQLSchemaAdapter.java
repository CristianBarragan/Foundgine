package com.foundgine.extensions.graphql;

import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.extensions.graphql.GraphQLSchemaDescriptor.GraphQLArgumentDescriptor;
import com.foundgine.extensions.graphql.GraphQLSchemaDescriptor.GraphQLFieldDescriptor;
import com.foundgine.extensions.graphql.GraphQLSchemaDescriptor.GraphQLInputFieldDescriptor;
import com.foundgine.extensions.graphql.GraphQLSchemaDescriptor.GraphQLInputTypeDescriptor;
import com.foundgine.extensions.graphql.GraphQLSchemaDescriptor.GraphQLObjectTypeDescriptor;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Objects;

/**
 * Builds the GraphQL schema vocabulary from Foundgine's semantic model. Introspection itself remains a responsibility of
 * the GraphQL host; this adapter supplies the schema that the host exposes and therefore keeps {@code __schema} /
 * {@code __type} outside Foundgine core. Port of {@code GraphQLSchemaAdapter}.
 */
public final class GraphQLSchemaAdapter {
    private static final Comparator<SemanticEntity> BY_NAME =
            Comparator.comparing(SemanticEntity::name, String.CASE_INSENSITIVE_ORDER);

    private final SemanticModel model;

    public GraphQLSchemaAdapter(SemanticModel model) {
        this.model = Objects.requireNonNull(model, "model");
    }

    public GraphQLSchemaDescriptor build() {
        var entities = sorted();
        var objects = entities.stream().map(this::buildObject).toList();
        var inputs = new ArrayList<GraphQLInputTypeDescriptor>();
        for (var e : entities) {
            inputs.add(buildInput(e));
            inputs.add(buildWhereInput(e));
        }
        var mutations = new ArrayList<GraphQLFieldDescriptor>();
        for (var e : entities) mutations.addAll(buildMutations(e));
        var queryFields = objects.stream()
                .map(t -> new GraphQLFieldDescriptor(toGraphQLName(t.name()), t.name(), false, true))
                .toList();
        return new GraphQLSchemaDescriptor(
                List.of(new GraphQLObjectTypeDescriptor("Query", queryFields)),
                List.of(new GraphQLObjectTypeDescriptor("Mutation", mutations)),
                inputs);
    }

    /** SDL suitable for registration with a GraphQL host, which can then expose standard introspection over it. */
    public String buildSdl() {
        var schema = build();
        var nl = System.lineSeparator();
        var lines = new ArrayList<String>();
        lines.add("type Query {");
        for (var f : schema.queryTypes().get(0).fields()) lines.add(formatField(f));
        lines.add("}");
        lines.add("");
        lines.add("type Mutation {");
        for (var f : schema.mutationTypes().get(0).fields()) lines.add(formatField(f));
        lines.add("}");

        for (var entity : sorted()) {
            lines.add("");
            lines.add("type " + entity.name() + " {");
            lines.add("  id: ID!");
            for (var field : entity.fields()) {
                if (field.id().equals(entity.identity().fieldId())) continue;
                lines.add("  " + toGraphQLName(field.name()) + ": " + GraphQLTypeMapper.map(field.clrType()));
            }
            for (var relationship : entity.relationships()) {
                var target = model.get(relationship.target());
                var type = relationship.cardinality() == RelationshipCardinality.MANY
                        ? "[" + target.name() + "!]!"
                        : target.name() + "!";
                lines.add("  " + toGraphQLName(relationship.name()) + ": " + type);
            }
            lines.add("}");
        }

        for (var input : schema.inputTypes()) {
            lines.add("");
            lines.add("input " + input.name() + " {");
            for (var f : input.fields())
                lines.add("  " + f.name() + ": " + formatType(f.type(), f.isList(), f.isNonNull()));
            lines.add("}");
        }
        return String.join(nl, lines) + nl;
    }

    private List<SemanticEntity> sorted() {
        return model.entities().stream().sorted(BY_NAME).toList();
    }

    private GraphQLObjectTypeDescriptor buildObject(SemanticEntity entity) {
        var fields = new ArrayList<GraphQLFieldDescriptor>();
        fields.add(new GraphQLFieldDescriptor("id", "ID", false, true));
        for (var f : entity.fields()) {
            if (f.id().equals(entity.identity().fieldId())) continue;
            fields.add(new GraphQLFieldDescriptor(toGraphQLName(f.name()), GraphQLTypeMapper.map(f.clrType())));
        }
        for (var r : entity.relationships()) {
            var target = model.get(r.target());
            fields.add(new GraphQLFieldDescriptor(toGraphQLName(r.name()), target.name(),
                    r.cardinality() == RelationshipCardinality.MANY, true));
        }
        return new GraphQLObjectTypeDescriptor(entity.name(), fields);
    }

    private GraphQLInputTypeDescriptor buildInput(SemanticEntity entity) {
        var fields = new ArrayList<GraphQLInputFieldDescriptor>();
        for (var f : entity.fields()) {
            if (f.id().equals(entity.identity().fieldId())) continue;
            fields.add(new GraphQLInputFieldDescriptor(toGraphQLName(f.name()), GraphQLTypeMapper.map(f.clrType())));
        }
        return new GraphQLInputTypeDescriptor(entity.name() + "Input", fields);
    }

    private GraphQLInputTypeDescriptor buildWhereInput(SemanticEntity entity) {
        var fields = new ArrayList<GraphQLInputFieldDescriptor>();
        fields.add(new GraphQLInputFieldDescriptor("id", "Int"));
        for (var f : entity.fields()) {
            if (f.id().equals(entity.identity().fieldId())) continue;
            fields.add(new GraphQLInputFieldDescriptor(toGraphQLName(f.name()), GraphQLTypeMapper.map(f.clrType())));
        }
        return new GraphQLInputTypeDescriptor(entity.name() + "WhereInput", fields);
    }

    private List<GraphQLFieldDescriptor> buildMutations(SemanticEntity entity) {
        var name = entity.name();
        var input = new GraphQLArgumentDescriptor("input", name + "Input", false, true);
        var where = new GraphQLArgumentDescriptor("where", name + "WhereInput", false, true);
        return List.of(
                new GraphQLFieldDescriptor("create" + name, name, false, true, List.of(input)),
                new GraphQLFieldDescriptor("update" + name, name, false, true, List.of(input, where)),
                new GraphQLFieldDescriptor("delete" + name, name, false, true, List.of(where)),
                new GraphQLFieldDescriptor("upsert" + name, name, false, true,
                        List.of(input, new GraphQLArgumentDescriptor("onConflict", "String", true, false))));
    }

    private static String formatField(GraphQLFieldDescriptor field) {
        var args = field.arguments().isEmpty()
                ? ""
                : "(" + String.join(", ", field.arguments().stream()
                        .map(a -> a.name() + ": " + formatType(a.type(), a.isList(), a.isNonNull())).toList()) + ")";
        return "  " + field.name() + args + ": " + formatType(field.type(), field.isList(), field.isNonNull());
    }

    private static String formatType(String type, boolean isList, boolean isNonNull) {
        var result = isList ? "[" + type + "!]" : type;
        return isNonNull ? result + "!" : result;
    }

    private static String toGraphQLName(String name) {
        if (name == null || name.isEmpty()) return name;
        return Character.toLowerCase(name.charAt(0)) + name.substring(1);
    }
}
