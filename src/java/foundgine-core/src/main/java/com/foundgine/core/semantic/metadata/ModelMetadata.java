package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.ModelId;

/** Static semantic model metadata. */
public record ModelMetadata(ModelId id, String name, EntityId entity, Integer minimumWeight) {
	public ModelMetadata {
		if (minimumWeight != null && (minimumWeight < 1 || minimumWeight > 100))
			throw new IllegalArgumentException("MinimumWeight must be between 1 and 100 (inclusive) when specified.");
	}

	public ModelMetadata(ModelId id, String name) {
		this(id, name, null, null);
	}

	public ModelMetadata(ModelId id, String name, EntityId entity) {
		this(id, name, entity, null);
	}
}
