package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.EntityId;
import java.util.*;

public record SemanticEntity(EntityId id, String name, SemanticFieldIdentity identity, List<SemanticField> fields,
		List<SemanticRelationship> relationships, List<SemanticAlias> aliases, Class<?> modelType) {
	public SemanticEntity {
		Objects.requireNonNull(id);
		Objects.requireNonNull(name);
		Objects.requireNonNull(identity);
		fields = List.copyOf(fields);
		relationships = List.copyOf(relationships);
		aliases = aliases == null ? List.of() : List.copyOf(aliases);
	}

	public SemanticEntity(EntityId id, String name, SemanticFieldIdentity identity, List<SemanticField> fields,
			List<SemanticRelationship> relationships) {
		this(id, name, identity, fields, relationships, List.of(), null);
	}

	public List<SemanticAlias> effectiveAliases() {
		return aliases;
	}
}
