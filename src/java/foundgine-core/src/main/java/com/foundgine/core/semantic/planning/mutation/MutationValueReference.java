package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.FieldId;

public record MutationValueReference(int sourceOperationIndex, FieldId sourceField) {
}
