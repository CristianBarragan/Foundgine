package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;

/** Semantic dependency between mutation operations. */
public record SemanticMutationDependency(int sourceOperationIndex, int targetOperationIndex,
                                         FieldId sourceField, FieldId targetField, RelationshipId relationship) {
    public SemanticMutationDependency { if (sourceField == null || targetField == null) throw new NullPointerException(); }
    public SemanticMutationDependency(int sourceOperationIndex, int targetOperationIndex, FieldId sourceField, FieldId targetField) {
        this(sourceOperationIndex, targetOperationIndex, sourceField, targetField, null);
    }
}
