package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.semantic.planning.mutation.MutationDependency;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationOperation;
import com.foundgine.core.semantic.planning.mutation.MutationValueReference;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Deterministic fingerprint of an {@link ExecutionMutationIR}, used to bind a
 * {@link MutationExecutionSecurityCertificate} to the exact IR it was issued
 * for.
 *
 * <p>C# builds an anonymous-object tree and hashes its
 * {@code System.Text.Json} serialization (explicitly avoiding serializing
 * {@code MutationEntitySchema} directly, since its {@code Fields} member is
 * keyed by {@code FieldId} and {@code FieldId} is a value object rather than
 * a JSON property-name type). This port instead follows this codebase's
 * established canonical-fingerprint convention (see
 * {@code SemanticModelFingerprint}): a length-prefixed delimited
 * {@link StringBuilder} representation, hashed with SHA-256. This achieves
 * the same goal — a deterministic fingerprint independent of any JSON
 * library's map/dictionary-key behavior — without depending on JSON at all.
 * Operation and dependency order is preserved as given (both are
 * execution-order-significant), while inherently unordered pieces (schema
 * columns, schema fields) are sorted for determinism, matching the original.
 */
final class MutationExecutionIRFingerprint {

    private MutationExecutionIRFingerprint() {
    }

    static String create(ExecutionMutationIR ir) {
        Objects.requireNonNull(ir);

        StringBuilder canonical = new StringBuilder("foundgine.mutation-execution-ir.v1\n");

        for (MutationOperation operation : ir.operations()) {
            appendEntity(canonical, operation.entity());
            append(canonical, "operation-kind", operation.kind().name());

            for (MutationFieldValue field : operation.fields()) {
                MutationValueReference source = field.source();
                String sourceText = source == null
                        ? ""
                        : source.sourceOperationIndex() + ":" + source.sourceField().value();
                append(canonical, "operation-field", field.column().value(), String.valueOf(field.value()), sourceText);
            }

            append(canonical, "operation-filter",
                    operation.filter() == null ? "" : String.valueOf(operation.filter()));

            List<ColumnId> conflictColumns = operation.conflictColumns();
            if (conflictColumns == null) {
                append(canonical, "operation-conflict-columns", "none");
            } else {
                conflictColumns.stream().map(ColumnId::value).sorted()
                        .forEach(value -> append(canonical, "operation-conflict-column", value));
            }

            List<FieldId> returnFields = operation.returnFields();
            if (returnFields == null) {
                append(canonical, "operation-return-fields", "none");
            } else {
                returnFields.stream().map(FieldId::value).sorted()
                        .forEach(value -> append(canonical, "operation-return-field", value));
            }
        }

        for (MutationDependency dependency : ir.dependencies()) {
            append(canonical, "dependency",
                    dependency.sourceOperationIndex(), dependency.targetOperationIndex(),
                    dependency.sourceField().value(), dependency.targetColumn().value());
        }

        ir.requiredSecurityInvariants().stream().sorted()
                .forEach(id -> append(canonical, "required-invariant", id));

        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] bytes = digest.digest(canonical.toString().getBytes(StandardCharsets.UTF_8));
            StringBuilder out = new StringBuilder(64);
            for (byte b : bytes)
                out.append(String.format("%02x", b));
            return out.toString();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private static void appendEntity(StringBuilder canonical, MutationEntitySchema entity) {
        EntityId id = entity.id();
        append(canonical, "operation-entity", id.value(), entity.name());

        entity.columns().stream().map(ColumnId::value).sorted()
                .forEach(value -> append(canonical, "operation-entity-column", value));

        entity.fields().entrySet().stream()
                .sorted(Comparator.comparingLong(e -> e.getKey().value()))
                .forEach(e -> {
                    Map.Entry<FieldId, ColumnId> entry = e;
                    ColumnId column = entry.getValue();
                    append(canonical, "operation-entity-field", entry.getKey().value(),
                            column == null ? "" : String.valueOf(column.value()));
                });

        ColumnId primaryKeyColumn = entity.primaryKeyColumn();
        append(canonical, "operation-entity-pk", primaryKeyColumn == null ? "" : String.valueOf(primaryKeyColumn.value()));
    }

    private static void append(StringBuilder builder, String kind, Object... values) {
        builder.append(kind);
        for (Object value : values) {
            String s = String.valueOf(value);
            builder.append('|').append(s.length()).append(':').append(s);
        }
        builder.append('\n');
    }
}
