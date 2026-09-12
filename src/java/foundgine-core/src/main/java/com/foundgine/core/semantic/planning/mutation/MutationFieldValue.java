package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;

public record MutationFieldValue(ColumnId column, Object value, MutationValueReference source) {
	public MutationFieldValue {
		if (column == null)
			throw new NullPointerException("column");
	}

	public MutationFieldValue(ColumnId column, Object value) {
		this(column, value, null);
	}

	public ColumnId columnId() {
		return column;
	}

	public static MutationFieldValue fromPrevious(ColumnId column, int sourceOperationIndex, FieldId sourceField) {
		return new MutationFieldValue(column, null, new MutationValueReference(sourceOperationIndex, sourceField));
	}
}
