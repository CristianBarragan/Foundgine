package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;

public record MutationDependency(int sourceOperationIndex, int targetOperationIndex, FieldId sourceField,
		ColumnId targetColumn) {
}
