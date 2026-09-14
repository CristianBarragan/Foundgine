package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.FieldId;
import java.util.*;

public record SemanticField(FieldId id, String name, Class<?> clrType, SemanticType semanticType, byte capabilities,
		List<SemanticAlias> aliases, List<SemanticConstraint> constraints, Boolean nullableOverride) {
	public SemanticField {
		Objects.requireNonNull(id);
		Objects.requireNonNull(name);
		Objects.requireNonNull(clrType);
		aliases = aliases == null ? List.of() : List.copyOf(aliases);
		constraints = constraints == null ? List.of() : List.copyOf(constraints);
	}

	public SemanticField(FieldId id, String name, Class<?> clrType) {
		this(id, name, clrType, null, SemanticFieldCapabilities.DEFAULT, List.of(), List.of(), null);
	}

	public List<SemanticAlias> effectiveAliases() {
		return aliases;
	}

	public List<SemanticConstraint> effectiveConstraints() {
		return constraints;
	}

	public SemanticType effectiveSemanticType() {
		return semanticType != null ? semanticType : SemanticType.fromClass(clrType);
	}

	public boolean isNullable() {
		return nullableOverride != null ? nullableOverride : (!clrType.isPrimitive());
	}
}
