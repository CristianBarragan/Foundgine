package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import java.util.List;

public record SemanticLexiconEntry(String canonicalName, SemanticLexicalCandidateKind kind, String searchText,
		EntityId entityId, RelationshipId relationshipId, FieldId fieldId, EntityId sourceEntityId,
		EntityId targetEntityId, String value, List<String> aliases, String description) {
	public SemanticLexiconEntry {
		aliases = aliases == null ? List.of() : List.copyOf(aliases);
	}

	public SemanticLexiconEntry(String canonicalName, SemanticLexicalCandidateKind kind, String searchText) {
		this(canonicalName, kind, searchText, null, null, null, null, null, null, List.of(), null);
	}

	public List<String> effectiveAliases() {
		return aliases;
	}
}
