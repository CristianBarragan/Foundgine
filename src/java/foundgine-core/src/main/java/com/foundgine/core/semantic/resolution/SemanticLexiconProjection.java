package com.foundgine.core.semantic.resolution;

import com.foundgine.core.semantic.*;
import java.util.*;

public final class SemanticLexiconProjection {
	private SemanticLexiconProjection() {
	}

	public static List<SemanticLexiconEntry> build(SemanticContractSnapshot contract) {
		Objects.requireNonNull(contract);
		var entries = new ArrayList<SemanticLexiconEntry>();
		for (var entity : contract.entities()) {
			var aliases = entity.effectiveAliases().stream().map(SemanticAlias::name).toList();
			entries.add(new SemanticLexiconEntry(entity.name(), SemanticLexicalCandidateKind.ENTITY, entity.name(),
					entity.id(), null, null, null, null, null, aliases, "Semantic entity " + entity.name() + "."));
			entries.add(new SemanticLexiconEntry(entity.name(), SemanticLexicalCandidateKind.NODE, entity.name(),
					entity.id(), null, null, null, null, null, aliases,
					"Semantic graph node for " + entity.name() + "."));
			for (var field : entity.fields()) {
				var fa = field.effectiveAliases().stream().map(SemanticAlias::name).toList();
				entries.add(new SemanticLexiconEntry(field.name(), SemanticLexicalCandidateKind.FIELD,
						entity.name() + " " + field.name(), entity.id(), null, field.id(), null, null, null, fa,
						"Field " + entity.name() + "." + field.name() + "."));
			}
			for (var relationship : entity.relationships()) {
				var target = contract.get(relationship.target());
				var ra = relationship.effectiveAliases().stream().map(SemanticAlias::name).toList();
				entries.add(new SemanticLexiconEntry(relationship.name(), SemanticLexicalCandidateKind.RELATIONSHIP,
						entity.name() + " " + relationship.name() + " " + target.name(), null, relationship.id(), null,
						entity.id(), relationship.target(), null, ra,
						"Relationship from " + entity.name() + " to " + target.name() + "."));
			}
		}
		for (var traversal : contract.traversals()) {
			var source = contract.get(traversal.source());
			var target = contract.get(traversal.target());
			entries.add(new SemanticLexiconEntry(traversal.name(), SemanticLexicalCandidateKind.TRAVERSAL,
					source.name() + " " + traversal.name() + " " + target.name(), null, null, null, traversal.source(),
					traversal.target(), null, List.of(),
					"Logical traversal from " + source.name() + " to " + target.name() + "."));
		}
		return List.copyOf(entries);
	}
}
