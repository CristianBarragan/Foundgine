package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Resolves a protocol-neutral SemanticRequest against the static semantic model
 * and produces the request SemanticGraph. Provider- and protocol-independent.
 */
public final class SemanticRequestResolver {
	private final SemanticContractSnapshot contract;

	public SemanticRequestResolver(SemanticContractSnapshot contract) {
		this.contract = Objects.requireNonNull(contract);
	}

	public SemanticGraph resolve(SemanticRequest request) {
		Objects.requireNonNull(request);
		var root = contract.get(request.root());
		if (request.selections().isEmpty())
			throw invalidSelection("A semantic request must contain at least one selection.");
		SemanticFilterValidator.validate(request.options() == null ? null : request.options().filter(), root, contract);
		var normalized = normalizeQueryOptions(request.options(), root);
		SemanticQueryOptionsValidator.validate(normalized, root);
		validateOrdering(normalized == null ? List.of() : normalized.effectiveOrder(), root, request.selections());

		var graph = new SemanticGraph();
		graph.setOptions(normalized);
		resolveSelections(root, request.selections(), graph, null, null, true);
		SemanticGraphValidator.validate(graph, contract);
		return graph;
	}

	private SemanticQueryOptions normalizeQueryOptions(SemanticQueryOptions options, SemanticEntity root) {
		if (options == null)
			return null;
		if (options.order() == null && options.after() == null)
			return options;
		var order = new ArrayList<SemanticOrderTerm>();
		for (var term : options.effectiveOrder())
			order.add(canonicalizeOrderTerm(term, root));
		if (options.after() != null) {
			var hasIdentity = order.stream().anyMatch(x -> x.path().isEmpty()
					&& x.aggregate() == SemanticOrderAggregate.NONE && x.field().equals(root.identity().fieldId()));
			if (!hasIdentity)
				order.add(new SemanticOrderTerm(root.identity().fieldId(), SemanticSortDirection.ASC));
		}
		return new SemanticQueryOptions(options.filter(), order, options.limit(), options.offset(), options.after());
	}

	private SemanticOrderTerm canonicalizeOrderTerm(SemanticOrderTerm term, SemanticEntity root) {
		if (term.aggregate() != SemanticOrderAggregate.COUNT || term.path().isEmpty())
			return term;
		var entity = root;
		for (var relationshipId : term.path()) {
			final var currentEntity = entity;
			var relationship = entity.relationships().stream().filter(x -> x.id().equals(relationshipId)).findFirst()
					.orElseThrow(() -> invalidSelection("Order relationship '" + relationshipId
							+ "' is not defined on '" + currentEntity.name() + "'."));
			entity = contract.get(relationship.target());
		}
		return new SemanticOrderTerm(entity.identity().fieldId(), term.direction(), term.path(), term.aggregate());
	}

	private void resolveSelections(SemanticEntity entity, List<SemanticSelection> selections, SemanticGraph graph,
			SemanticGraph.SemanticGraphNode parent, RelationshipId viaRelationship, boolean root) {
		var fields = new ArrayList<FieldId>();
		var relationships = new ArrayList<RelationshipSelection>();
		var relationshipIds = new HashSet<RelationshipId>();
		for (var selection : selections) {
			if (selection.field() != null && selection.relationship() != null)
				throw invalidSelection("A selection cannot contain both a field and a relationship.");
			if (selection.field() == null && selection.relationship() == null)
				throw invalidSelection("A selection must contain a field or a relationship.");
			if (selection.field() != null) {
				if (!selection.children().isEmpty())
					throw invalidSelection("Field '" + selection.field() + "' cannot have child selections.");
				if (!isDeclaredField(entity, selection.field()))
					throw invalidSelection(
							"Entity '" + entity.name() + "' does not declare field '" + selection.field() + "'.");
				if (!isFieldSelectable(entity, selection.field()))
					throw invalidSelection(
							"Field '" + entity.name() + "." + selection.field() + "' is not selectable.");
				if (!fields.contains(selection.field()))
					fields.add(selection.field());
			} else {
				var relationshipId = selection.relationship();
				if (!relationshipIds.add(relationshipId))
					throw invalidSelection("Relationship '" + relationshipId + "' is selected more than once on '"
							+ entity.name() + "'. Merge repeated selections in the adapter before resolution.");
				var relationship = entity.relationships().stream().filter(r -> r.id().equals(relationshipId))
						.findFirst().orElse(null);
				if (relationship == null)
					throw invalidSelection(
							"Entity '" + entity.name() + "' does not declare relationship '" + relationshipId + "'.");
				relationships.add(new RelationshipSelection(relationship, selection.children()));
			}
		}
		var node = root ? graph.addRoot(entity.id(), fields) : graph.add(entity.id(), viaRelationship, parent, fields);
		for (var selected : relationships) {
			var target = contract.get(selected.relationship().target());
			if (selected.children().isEmpty())
				throw invalidSelection("Relationship '" + entity.name() + "." + selected.relationship().name()
						+ "' requires child selections.");
			resolveSelections(target, selected.children(), graph, node, selected.relationship().id(), false);
		}
	}

	private void validateOrdering(List<SemanticOrderTerm> terms, SemanticEntity root,
			List<SemanticSelection> selections) {
		for (var term : terms) {
			var entity = root;
			var currentSelections = selections;
			SemanticRelationship finalRelationship = null;
			for (var relationshipId : term.path()) {
				final var currentEntity = entity;
				var relationship = entity.relationships().stream().filter(r -> r.id().equals(relationshipId))
						.findFirst().orElseThrow(() -> invalidSelection("Order relationship '" + relationshipId
								+ "' is not defined on '" + currentEntity.name() + "'."));
				if (relationship.cardinality() == RelationshipCardinality.MANY) {
					if (!relationshipId.equals(term.path().get(term.path().size() - 1)) || !term.isAggregate())
						throw new UnsupportedOperationException(
								"Ordering through collection relationship '" + entity.name() + "." + relationship.name()
										+ "' requires an explicit aggregate semantics (COUNT, MIN, or MAX).");
					if (term.aggregate() == SemanticOrderAggregate.MIN
							|| term.aggregate() == SemanticOrderAggregate.MAX) {
						var target = contract.get(relationship.target());
						if (!isDeclaredField(target, term.field()))
							throw invalidSelection("Aggregate order field '" + term.field() + "' is not defined on '"
									+ target.name() + "'.");
					}
				}
				finalRelationship = relationship;
				var aggregateCollectionHop = relationship.cardinality() == RelationshipCardinality.MANY
						&& term.isAggregate() && relationshipId.equals(term.path().get(term.path().size() - 1));
				if (!aggregateCollectionHop && !isRelationshipSelected(currentSelections, relationshipId))
					throw new IllegalStateException("Order path '"
							+ String.join(".", term.path().stream().map(Object::toString).toList())
							+ "' requires relationship '" + entity.name() + "." + relationship.name()
							+ "' to be selected. The provider will not introduce an implicit join solely for ordering.");
				entity = contract.get(relationship.target());
				currentSelections = findRelationshipSelections(currentSelections, relationshipId);
			}
			if (term.isAggregate()) {
				if (term.path().isEmpty())
					throw invalidSelection("Aggregate ordering requires a relationship path.");
				if (finalRelationship == null)
					throw invalidSelection("Aggregate order relationship '" + term.path().get(term.path().size() - 1)
							+ "' is not defined.");
				if (finalRelationship.cardinality() != RelationshipCardinality.MANY)
					throw invalidSelection("Aggregate ordering is only valid on collection relationships.");
				if ((term.aggregate() == SemanticOrderAggregate.MIN || term.aggregate() == SemanticOrderAggregate.MAX)
						&& !isFieldSortable(entity, term.field()))
					throw invalidSelection("Aggregate order field '" + term.field() + "' is not sortable.");
			} else if (!isDeclaredField(entity, term.field()))
				throw invalidSelection("Order field '" + term.field() + "' is not defined on '" + entity.name() + "'.");
			else if (!isFieldSortable(entity, term.field()))
				throw invalidSelection("Field '" + entity.name() + "." + term.field() + "' is not sortable.");
		}
	}

	private static boolean isFieldSelectable(SemanticEntity entity, FieldId fieldId) {
		if (entity.identity().fieldId().equals(fieldId))
			return true;
		return entity.fields().stream().anyMatch(
				x -> x.id().equals(fieldId) && (x.capabilities() & SemanticFieldCapabilities.SELECTABLE) != 0);
	}

	private static boolean isFieldSortable(SemanticEntity entity, FieldId fieldId) {
		if (entity.identity().fieldId().equals(fieldId))
			return true;
		return entity.fields().stream()
				.anyMatch(x -> x.id().equals(fieldId) && (x.capabilities() & SemanticFieldCapabilities.SORTABLE) != 0);
	}

	private static boolean isDeclaredField(SemanticEntity entity, FieldId fieldId) {
		return entity.identity().fieldId().equals(fieldId)
				|| entity.fields().stream().anyMatch(f -> f.id().equals(fieldId));
	}

	private static boolean isRelationshipSelected(List<SemanticSelection> selections, RelationshipId id) {
		return selections.stream().anyMatch(s -> id.equals(s.relationship()));
	}

	private static List<SemanticSelection> findRelationshipSelections(List<SemanticSelection> selections,
			RelationshipId id) {
		return selections.stream().filter(s -> id.equals(s.relationship())).findFirst().map(SemanticSelection::children)
				.orElse(List.of());
	}

	private static IllegalStateException invalidSelection(String message) {
		return new IllegalStateException("Invalid semantic request: " + message);
	}

	private record RelationshipSelection(SemanticRelationship relationship, List<SemanticSelection> children) {
	}
}
