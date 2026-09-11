package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.mutation.SemanticMutationPlan;
import com.foundgine.core.semantic.mutation.SemanticMutationPlan.SemanticMutationDependencyPlan;
import com.foundgine.core.semantic.mutation.SemanticMutationPlan.SemanticMutationOperationPlan;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Core.Semantic.Planning.Mutation.SemanticMutationExecutionLowerer}.
 *
 * <p>Lowers a semantic mutation plan into provider-neutral execution work.
 * This is the first boundary where semantic {@code FieldId} values are
 * resolved to physical {@code ColumnId} values. Provider-specific SQL remains
 * outside this type.
 *
 * <p>C# declares this type in the {@code Execution/Mutation} source file but
 * under the {@code Foundgine.Core.Semantic.Planning.Mutation} namespace; this
 * port follows the namespace (matching this codebase's package-mirrors-
 * namespace convention), not the C# file's location on disk.
 */
public final class SemanticMutationExecutionLowerer {

    private final MutationSchema schema;

    public SemanticMutationExecutionLowerer(MutationSchema schema) {
        this.schema = Objects.requireNonNull(schema);
    }

    public ExecutionMutationIR lower(SemanticMutationPlan plan) {
        Objects.requireNonNull(plan);
        if (plan.operations().isEmpty())
            throw new IllegalStateException("A semantic mutation plan must contain at least one operation.");

        List<MutationOperation> operations = new ArrayList<>(plan.operations().size());

        for (SemanticMutationOperationPlan operation : plan.operations()) {
            MutationEntitySchema entity = schema.getEntity(operation.entity());
            List<MutationFieldValue> fields = new ArrayList<>(operation.fields().size());

            for (var field : operation.fields()) {
                ColumnId column = entity.fields().get(field.field());
                if (column == null)
                    throw new IllegalStateException(
                            "Semantic mutation field '" + field.field().value() + "' is not writable on '" + entity.name() + "'.");

                MutationValueReference source = field.source() == null
                        ? null
                        : new MutationValueReference(field.source().sourceOperationIndex(), field.source().sourceField());

                fields.add(new MutationFieldValue(column, field.value(), source));
            }

            List<ColumnId> conflicts = null;
            if (operation.kind() == SemanticMutationKind.UPSERT) {
                List<ColumnId> mapped = new ArrayList<>(operation.conflictFields().size());
                for (var field : operation.conflictFields()) {
                    ColumnId column = entity.fields().get(field);
                    if (column == null)
                        throw new IllegalStateException(
                                "Semantic conflict field '" + field.value() + "' is not mapped on '" + entity.name() + "'.");
                    mapped.add(column);
                }

                if (mapped.isEmpty())
                    throw new IllegalStateException("Upsert for '" + entity.name() + "' requires semantic conflict fields.");

                conflicts = mapped;
            }

            validate(operation, entity);

            MutationKind kind = switch (operation.kind()) {
                case CREATE -> MutationKind.CREATE;
                case UPDATE -> MutationKind.UPDATE;
                case DELETE -> MutationKind.DELETE;
                case UPSERT -> MutationKind.UPSERT;
            };

            operations.add(new MutationOperation(entity, kind, fields, operation.filter(), conflicts, operation.returnFields()));
        }

        // Semantic dependencies are the single source of truth. At this boundary
        // semantic FieldIds are resolved to physical target ColumnIds. The
        // provider-specific correlation carrier is introduced later by the SQL
        // compiler, not represented as a second semantic edge collection.
        List<MutationDependency> dependencies = new ArrayList<>(plan.dependencies().size());
        for (SemanticMutationDependencyPlan dependency : plan.dependencies()) {
            int source;
            int target;
            try {
                source = Integer.parseInt(dependency.fromOperationId());
                target = Integer.parseInt(dependency.toOperationId());
            } catch (NumberFormatException e) {
                throw new IllegalStateException(
                        "Semantic mutation operation IDs must be stable numeric ordinals for execution lowering.");
            }

            if (source < 0 || source >= operations.size() || target < 0 || target >= operations.size() || source >= target)
                throw new IllegalStateException("Invalid semantic mutation dependency " + source + " -> " + target + ".");

            MutationEntitySchema targetEntity = operations.get(target).entity();
            ColumnId targetColumn = targetEntity.fields().get(dependency.targetField());
            if (targetColumn == null)
                throw new IllegalStateException(
                        "Semantic dependency target field '" + dependency.targetField().value() + "' is not writable on '" + targetEntity.name() + "'.");

            dependencies.add(new MutationDependency(source, target, dependency.sourceField(), targetColumn));
        }

        return ExecutionMutationIR.from(
                new MutationBatchPlan(operations, dependencies),
                plan.requiredSecurityInvariants());
    }

    private static void validate(SemanticMutationOperationPlan operation, MutationEntitySchema entity) {
        if ((operation.kind() == SemanticMutationKind.UPDATE || operation.kind() == SemanticMutationKind.DELETE)
                && operation.filter() == null) {
            throw new IllegalStateException(
                    "Unfiltered " + operation.kind() + " mutations are not permitted for '" + entity.name() + "'.");
        }

        if (operation.kind() == SemanticMutationKind.DELETE && !operation.fields().isEmpty())
            throw new IllegalStateException("Delete mutations cannot contain field values.");

        if (operation.kind() != SemanticMutationKind.DELETE && operation.fields().isEmpty())
            throw new IllegalStateException(
                    operation.kind() + " mutations must contain at least one field value.");

        for (var field : operation.returnFields())
            if (!entity.fields().containsKey(field))
                throw new IllegalStateException(
                        "Return field '" + field.value() + "' is not registered on '" + entity.name() + "'.");
    }
}
