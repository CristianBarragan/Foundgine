package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.intent.*;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Converts a committed lexical interpretation into a provider-neutral read
 * intent.
 */
public final class SemanticLexicalReadIntentGrounder {
	private final SemanticContractSnapshot contract;
	private final SemanticLexicalResolver resolver;

	public SemanticLexicalReadIntentGrounder(SemanticContractSnapshot contract, SemanticLexicalResolver resolver) {
		this.contract = Objects.requireNonNull(contract);
		this.resolver = Objects.requireNonNull(resolver);
	}

	public ReadIntent ground(String expression) {
		return ground(expression, () -> false);
	}

	public ReadIntent ground(String expression, ISemanticLexicalCandidateSource.CancellationToken cancellation) {
		String normalized = normalizeIntentExpression(expression);
		GroundingDecision decision = resolver.ground(normalized, cancellation);
		if (decision.outcome() != GroundingOutcome.COMMITTED || decision.committed() == null)
			throw new IllegalStateException(
					"Lexical intent could not be committed: " + decision.outcome() + ". " + decision.reason());
		return buildIntent(decision.committed());
	}

	private static String normalizeIntentExpression(String expression) {
		Set<String> ignored = new HashSet<>(Arrays.asList("show", "display", "list", "find", "get", "give", "return",
				"fetch", "please", "me", "my", "the", "a", "an", "all", "some", "what", "which", "who", "where", "that",
				"those", "these", "are", "is", "currently", "current", "from", "with", "for", "of", "to", "in", "on",
				"and", "by"));
		return Arrays.stream(expression.split("\\s+")).map(x -> x.replaceAll("^[,.;:?!]+|[,.;:?!]+$", ""))
				.filter(x -> !x.isEmpty() && !ignored.contains(x.toLowerCase(Locale.ROOT)))
				.collect(java.util.stream.Collectors.joining(" "));
	}

	private ReadIntent buildIntent(GroundingInterpretation interpretation) {
		SemanticEntity root = contract.get(interpretation.rootEntity());
		var selections = new ArrayList<ReadSelection>();
		var filters = new ArrayList<ReadFilter>();
		for (var step : interpretation.steps()) {
			var c = step.candidate();
			if (c.kind() == SemanticLexicalCandidateKind.RELATIONSHIP
					|| c.kind() == SemanticLexicalCandidateKind.TRAVERSAL) {
				if (c.relationshipId() == null || c.sourceEntityId() == null)
					continue;
				var route = findEntityPath(root.id(), c.sourceEntityId());
				route.add(c.relationshipId());
				addSelectionPath(selections, root.id(), route, null);
				continue;
			}
			if (c.kind() == SemanticLexicalCandidateKind.FIELD && c.fieldId() != null && c.entityId() != null) {
				var route = findEntityPath(root.id(), c.entityId());
				addSelectionPath(selections, root.id(), route, c.fieldId());
				continue;
			}
			if (c.kind() == SemanticLexicalCandidateKind.VALUE) {
				if (c.fieldId() == null || c.entityId() == null)
					throw new IllegalStateException("Lexical value '" + c.canonicalName()
							+ "' has no semantic field binding; intent was not committed to execution.");
				var route = findEntityPath(root.id(), c.entityId());
				filters.add(buildValueFilter(route, c.entityId(), c.fieldId(), c.value()));
				addSelectionPath(selections, root.id(), route, c.fieldId());
			}
		}
		if (selections.isEmpty())
			selections.add(new ReadSelection(root.identity().name()));
		return new ReadIntent(root.name(), selections,
				filters.isEmpty() ? null : filters.size() == 1 ? filters.get(0) : new ReadAndFilter(filters), List.of(),
				null, null, null, null);
	}

	private ReadFilter buildValueFilter(List<RelationshipId> route, EntityId owner, FieldId field, String value) {
		SemanticEntity target = contract.get(owner);
		String fieldName = findField(target, field).name();
		ReadFilter result = new ReadFieldFilter(fieldName, SemanticFilterOperator.EQ, value);
		for (int i = route.size() - 1; i >= 0; i--)
			result = new ReadRelationshipFilter(findRelationshipById(route.get(i)).name(),
					SemanticRelationshipQuantifier.SOME, result);
		return result;
	}

	private void addSelectionPath(List<ReadSelection> selections, EntityId rootId, List<RelationshipId> route,
			FieldId field) {
		if (route.isEmpty()) {
			if (field == null)
				return;
			String name = findField(contract.get(rootId), field).name();
			if (selections.stream().noneMatch(x -> x.field() != null && x.field().equalsIgnoreCase(name)))
				selections.add(new ReadSelection(name));
			return;
		}
		List<ReadSelection> current = selections;
		for (int i = 0; i < route.size(); i++) {
			var rel = findRelationshipById(route.get(i));
			var existing = current.stream()
					.filter(x -> x.relationship() != null && x.relationship().equalsIgnoreCase(rel.name())).findFirst()
					.orElse(null);
			if (existing == null) {
				existing = new ReadSelection(null, rel.name(), List.of());
				current.add(existing);
			}
			int idx = current.indexOf(existing);
			var children = new ArrayList<>(existing.effectiveChildren());
			if (i == route.size() - 1) {
				var target = contract.get(rel.target());
				String fieldName = field != null ? findField(target, field).name() : target.identity().name();
				if (children.stream().noneMatch(x -> x.field() != null && x.field().equalsIgnoreCase(fieldName)))
					children.add(new ReadSelection(fieldName));
			}
			var replacement = new ReadSelection(existing.field(), existing.relationship(), children);
			current.set(idx, replacement);
			current = children;
		}
	}

	private SemanticRelationship findRelationshipById(RelationshipId id) {
		return contract.entities().stream().flatMap(x -> x.relationships().stream()).filter(x -> x.id().equals(id))
				.findFirst()
				.orElseThrow(() -> new IllegalStateException("Semantic relationship " + id + " is not defined."));
	}

	private List<RelationshipId> findEntityPath(EntityId source, EntityId target) {
		if (source.equals(target))
			return new ArrayList<>();
		record Node(EntityId id, List<RelationshipId> path) {
		}
		var q = new ArrayDeque<Node>();
		var visited = new HashSet<EntityId>();
		q.add(new Node(source, List.of()));
		visited.add(source);
		while (!q.isEmpty()) {
			var n = q.remove();
			for (var r : contract.get(n.id()).relationships()) {
				var p = new ArrayList<>(n.path());
				p.add(r.id());
				if (r.target().equals(target))
					return p;
				if (visited.add(r.target()))
					q.add(new Node(r.target(), p));
			}
		}
		throw new IllegalStateException("No semantic path connects entity " + source + " to entity " + target + ".");
	}

	private SemanticField findField(SemanticEntity entity, FieldId id) {
		return entity.fields().stream().filter(x -> x.id().equals(id)).findFirst().orElseGet(() -> {
			if (entity.identity().fieldId().equals(id))
				return new SemanticField(entity.identity().fieldId(), entity.identity().name(), Object.class);
			throw new IllegalStateException("Field " + id + " is not defined on '" + entity.name() + "'.");
		});
	}
}
