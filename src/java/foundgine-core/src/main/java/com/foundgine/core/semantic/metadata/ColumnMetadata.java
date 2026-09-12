package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.ColumnId;

public record ColumnMetadata(ColumnId id, String name, String storageName) {
	public ColumnMetadata(ColumnId id, String name) {
		this(id, name, null);
	}

	public String effectiveStorageName() {
		return storageName != null ? storageName : name;
	}
}
