package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.abstractions.MutationRelationshipSchema;
import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.semantic.mutation.SemanticMutationBuilder;
import com.foundgine.core.semantic.mutation.SemanticMutationDependency;
import com.foundgine.core.semantic.mutation.SemanticMutationField;
import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.mutation.SemanticMutationValueReference;
import com.foundgine.core.semantic.planning.mutation.IMutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.NestedMutationIntent;
import com.foundgine.core.semantic.planning.mutation.UpsertIntent;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Port of C# {@code GraphQLMutationSemanticConverter}. Converts the GraphQL adapter's nested intent representation into
 * the canonical semantic mutation graph. Security-sensitive GraphQL execution must use this boundary so the mutation
 * engine can apply warrant validation, policy authorization and the final execution security gate to the exact
 * operation graph that came from GraphQL.
 *
 * <p>Parity note: C# resolves a column's field with {@code FirstOrDefault(...).Key}, which yields {@code default(FieldId)}
 * (value 0) when no field maps to the column. That behaviour is reproduced exactly via {@link #fieldFor}.
 */
public final class GraphQLMutationSemanticConverter {
    private static final FieldId DEFAULT_FIELD = new FieldId(0);

    private GraphQLMutationSemanticConverter() {}

    public static SemanticMutationOperationGraph toSemanticGraph(
            List<NestedMutationIntent> intents, MutationSchema schema) {
        Objects.requireNonNull(intents, "intents");
        Objects.requireNonNull(schema, "schema");
        if (intents.isEmpty())
            throw new IllegalStateException("A GraphQL mutation batch must contain at least one mutation intent.");

        var operations = new ArrayList<SemanticMutationOperation>();
        for (var intent : intents) visit(intent, null, null, operations, schema);
        return new SemanticMutationOperationGraph(operations);
    }

    private static void visit(
            NestedMutationIntent intent,
            Integer parentIndex,
            MutationRelationshipSchema relationship,
            List<SemanticMutationOperation> operations,
            MutationSchema schema) {
        int index = operations.size();
        var operation = convertOperation(intent.mutation(), schema);
        operations.add(operation);

        if (parentIndex != null) {
            int parent = parentIndex;
            if (relationship == null)
                throw new IllegalStateException("Nested GraphQL mutation relationship metadata is missing.");

            var parentOperation = operations.get(parent);
            if (!relationship.source().equals(parentOperation.entity())
                    || !relationship.target().equals(operation.entity())) {
                throw new IllegalStateException("GraphQL nested mutation relationship '" + relationship.name()
                        + "' does not connect '" + id(parentOperation.entity().value()) + "' to '"
                        + id(operation.entity().value()) + "'.");
            }

            var parentSchema = schema.getEntity(parentOperation.entity());
            var primaryKeyColumn = parentSchema.primaryKeyColumn();
            if (primaryKeyColumn == null)
                throw new IllegalStateException("Parent entity '" + parentSchema.name()
                        + "' requires a primary key for nested mutation propagation.");

            if (!primaryKeyColumn.equals(relationship.sourceColumn()))
                throw new IllegalStateException("Nested mutation relationship '" + relationship.name()
                        + "' must originate at the parent primary key.");

            var parentPkField = fieldFor(parentSchema.fields(), primaryKeyColumn);
            if (parentPkField.equals(DEFAULT_FIELD))
                throw new IllegalStateException("Parent primary key column '" + id(primaryKeyColumn.value())
                        + "' has no semantic field mapping.");

            var childSchema = schema.getEntity(operation.entity());
            var childField = fieldFor(childSchema.fields(), relationship.targetColumn());
            if (childField.equals(DEFAULT_FIELD))
                throw new IllegalStateException("Nested relationship target column '"
                        + id(relationship.targetColumn().value()) + "' has no semantic field mapping.");

            if (!parentOperation.returnFields().contains(parentPkField))
                throw new IllegalStateException("Parent mutation '" + parentSchema.name()
                        + "' must return its primary key field '" + id(parentPkField.value())
                        + "' for nested propagation.");

            var fields = new ArrayList<>(operation.fields());
            if (fields.stream().anyMatch(x -> x.field().equals(childField) && x.source() == null))
                throw new IllegalStateException("Nested mutation '" + childSchema.name()
                        + "' explicitly supplies relationship field '" + id(childField.value()) + "'.");

            if (fields.stream().noneMatch(x -> x.field().equals(childField) && x.source() != null))
                fields.add(new SemanticMutationField(
                        childField, null, new SemanticMutationValueReference(parent, parentPkField)));

            var dependencies = new ArrayList<>(operation.dependencies());
            dependencies.add(new SemanticMutationDependency(parent, index, parentPkField, childField, relationship.id()));

            operation = new SemanticMutationOperation(
                    operation.entity(),
                    operation.kind(),
                    fields,
                    operation.filter(),
                    operation.conflictFields(),
                    operation.returnFields(),
                    operation.effects(),
                    dependencies);
            operations.set(index, operation);
        }

        for (var child : intent.children()) {
            var relation = schema.getRelationship(child.relationship());
            visit(child.mutation(), index, relation, operations, schema);
        }
    }

    private static SemanticMutationOperation convertOperation(IMutationIntent intent, MutationSchema schema) {
        var entity = schema.getEntity(intent.entity());

        if (intent instanceof MutationIntent mutation) {
            var fields = toSemanticFields(entity, mutation.fields());
            return switch (mutation.kind()) {
                case CREATE -> SemanticMutationBuilder.create(entity.id(), fields, mutation.returnFields());
                case UPDATE -> SemanticMutationBuilder.update(
                        entity.id(), fields, mutation.filter(), mutation.returnFields());
                case DELETE -> SemanticMutationBuilder.delete(
                        entity.id(),
                        requireFilter(mutation),
                        mutation.returnFields());
                default -> throw new UnsupportedOperationException(
                        "Mutation kind '" + mutation.kind() + "' is not supported.");
            };
        }

        if (intent instanceof UpsertIntent upsert) {
            var fields = toSemanticFields(entity, upsert.fields());
            List<FieldId> conflicts = new ArrayList<>();
            if (upsert.conflictColumns() != null)
                for (var column : upsert.conflictColumns()) conflicts.add(fieldFor(entity.fields(), column));
            return SemanticMutationBuilder.upsert(entity.id(), fields, conflicts, upsert.returnFields());
        }

        throw new UnsupportedOperationException(
                "GraphQL mutation intent '" + intent.getClass().getSimpleName() + "' is not supported.");
    }

    private static com.foundgine.core.semantic.query.SemanticFilterExpression requireFilter(MutationIntent mutation) {
        if (mutation.filter() == null) throw new IllegalStateException("Delete mutation requires a filter.");
        return mutation.filter();
    }

    private static List<SemanticMutationField> toSemanticFields(
            MutationEntitySchema entity, List<MutationFieldValue> values) {
        var result = new ArrayList<SemanticMutationField>(values.size());
        for (var value : values) result.add(toSemanticField(entity, value));
        return result;
    }

    private static SemanticMutationField toSemanticField(MutationEntitySchema entity, MutationFieldValue field) {
        var semanticField = fieldFor(entity.fields(), field.column());
        if (semanticField.equals(DEFAULT_FIELD))
            throw new IllegalStateException("GraphQL mutation column '" + id(field.column().value())
                    + "' is not mapped to a semantic field on '" + entity.name() + "'.");

        var reference = field.source();
        var source = reference == null
                ? null
                : new SemanticMutationValueReference(reference.sourceOperationIndex(), reference.sourceField());
        return new SemanticMutationField(semanticField, field.value(), source);
    }

    /** {@code entity.Fields.FirstOrDefault(x => x.Value == column).Key}: first match in map order, else default(FieldId). */
    private static FieldId fieldFor(Map<FieldId, ColumnId> fields, ColumnId column) {
        for (var entry : fields.entrySet())
            if (Objects.equals(entry.getValue(), column)) return entry.getKey();
        return DEFAULT_FIELD;
    }

    /** C# identities are unsigned 64-bit; print them the same way. */
    private static String id(long value) {
        return Long.toUnsignedString(value);
    }
}
