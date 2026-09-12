package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.EntityId;
import java.util.List;

/** Static description of a domain entity. Metadata describes what exists. */
public record EntityMetadata(EntityId entityId, String name, List<ColumnMetadata> columns, String storageName,
		List<FieldMetadata> fields, ColumnReference primaryKey, Class<?> clrType, boolean event,
		ColumnReference temporalColumn, List<AliasDeclaration> aliases) {
	public EntityMetadata(EntityId entityId, String name, List<ColumnMetadata> columns) {
		this(entityId, name, columns, null, null, null, null, false, null, null);
	}

	public String effectiveStorageName() {
		return storageName != null ? storageName : name;
	}

	public List<FieldMetadata> effectiveFields() {
		return fields != null ? fields : List.of();
	}

	public List<AliasDeclaration> effectiveAliases() {
		return aliases == null ? List.of() : List.copyOf(aliases);
	}
}
