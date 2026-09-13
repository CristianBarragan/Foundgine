package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** Proves that a request graph is consistent with the semantic contract. */
public final class SemanticGraphValidator {
	private SemanticGraphValidator() {
	}

	public static void validate(SemanticGraph graph, SemanticContractSnapshot contract) {
		validate(graph, contract, SemanticGraphValidationMode.STRICT);
	}

	public static void validate(SemanticGraph graph, SemanticContractSnapshot contract,
			SemanticGraphValidationMode mode) {
		Objects.requireNonNull(graph);
		Objects.requireNonNull(contract);
		validateCore(graph, contract, mode);
	}

	public static void validate(SemanticGraph graph, SemanticModel model) {
		validate(graph, model, SemanticGraphValidationMode.STRICT);
	}

	public static void validate(SemanticGraph graph, SemanticModel model, SemanticGraphValidationMode mode) {
		Objects.requireNonNull(graph);
		Objects.requireNonNull(model);
		validateCore(graph, model, mode);
	}

	private static void validateCore(SemanticGraph graph, SemanticModel model, SemanticGraphValidationMode mode) {
		if (graph.nodes().isEmpty())
			throw new IllegalStateException("A semantic graph cannot be empty.");
		validateIdsAndReachability(graph, mode);
		var byId = graph.nodes().stream()
				.collect(java.util.stream.Collectors.toMap(SemanticGraph.SemanticGraphNode::id, x -> x));
		for (var node : graph.nodes())
			validateNode(node, byId, model, mode);
	}

	private static void validateCore(SemanticGraph graph, SemanticContractSnapshot contract,
			SemanticGraphValidationMode mode) {
		if (graph.nodes().isEmpty())
			throw new IllegalStateException("A semantic graph cannot be empty.");
		validateIdsAndReachability(graph, mode);
		var byId = graph.nodes().stream()
				.collect(java.util.stream.Collectors.toMap(SemanticGraph.SemanticGraphNode::id, x -> x));
		for (var node : graph.nodes())
			validateNode(node, byId, contract, mode);
	}

	private static void validateIdsAndReachability(SemanticGraph graph, SemanticGraphValidationMode mode) {
		var ids = graph.nodes().stream().map(SemanticGraph.SemanticGraphNode::id).toList();
		if (new HashSet<>(ids).size() != ids.size())
			throw new IllegalStateException("A semantic graph contains duplicate node identities.");
		var idSet = new HashSet<>(ids);
		var roots = graph.nodes().stream().filter(n -> n.parentId() == null).toList();
		if (mode == SemanticGraphValidationMode.STRICT && roots.size() != 1)
			throw new IllegalStateException("A semantic graph must contain exactly one root node.");
		if (mode != SemanticGraphValidationMode.STRICT && roots.isEmpty())
			throw new IllegalStateException("A semantic graph must contain at least one root node.");
		var children = new HashMap<Integer, List<SemanticGraph.SemanticGraphNode>>();
		for (var n : graph.nodes())
			if (n.parentId() != null) {
				if (!idSet.contains(n.parentId()))
					throw new IllegalStateException(
							"Semantic node " + n.id() + " references missing parent node " + n.parentId() + ".");
				children.computeIfAbsent(n.parentId(), k -> new ArrayList<>()).add(n);
			}
		var visited = new HashSet<Integer>();
		for (var root : roots)
			visit(root, children, visited);
		if (visited.size() != graph.nodes().size() && mode != SemanticGraphValidationMode.EXPLORATORY)
			throw new IllegalStateException("Semantic graph contains unreachable nodes.");
	}

	private static void visit(SemanticGraph.SemanticGraphNode node,
			Map<Integer, List<SemanticGraph.SemanticGraphNode>> children, Set<Integer> visited) {
		if (!visited.add(node.id()))
			throw new IllegalStateException("Semantic graph contains a cycle at node " + node.id() + ".");
		for (var child : children.getOrDefault(node.id(), List.of()))
			visit(child, children, visited);
	}

	private static void validateNode(SemanticGraph.SemanticGraphNode node,
			Map<Integer, SemanticGraph.SemanticGraphNode> byId, SemanticModel model, SemanticGraphValidationMode mode) {
		var entity = model.entities().stream().filter(e -> e.id().equals(node.entityId())).findFirst().orElse(null);
		if (node.parentId() != null) {
			var parent = byId.get(node.parentId());
			if (node.viaRelationship() == null && node.viaConnection() == null)
				throw new IllegalStateException(
						"Non-root semantic node " + node.id() + " must specify a relationship or connection.");
			if (node.viaRelationship() != null && node.viaConnection() != null)
				throw new IllegalStateException(
						"Semantic node " + node.id() + " cannot specify both relationship and connection edges.");
			if (node.viaRelationship() != null && parent != null && entity != null)
				validateRelationship(parent.entityId(), node.viaRelationship(), node.entityId(), model, mode,
						node.id());
		} else if (node.viaRelationship() != null || node.viaConnection() != null)
			throw new IllegalStateException("Root semantic node " + node.id() + " cannot specify a parent edge.");
		if (entity == null) {
			if (mode == SemanticGraphValidationMode.FEDERATED || mode == SemanticGraphValidationMode.EXPLORATORY)
				return;
			throw new IllegalStateException(
					"Semantic node " + node.id() + " references unknown entity '" + node.entityId() + "'.");
		}
		for (var field : new HashSet<>(node.fields()))
			if (!field.equals(entity.identity().fieldId())
					&& entity.fields().stream().noneMatch(f -> f.id().equals(field))) {
				if (mode == SemanticGraphValidationMode.EXPLORATORY)
					continue;
				throw new IllegalStateException("Semantic node " + node.id() + " selects unknown field '" + field
						+ "' on '" + entity.name() + "'.");
			}
	}

	private static void validateNode(SemanticGraph.SemanticGraphNode node,
			Map<Integer, SemanticGraph.SemanticGraphNode> byId, SemanticContractSnapshot contract,
			SemanticGraphValidationMode mode) {
		var entity = contract.entities().stream().filter(e -> e.id().equals(node.entityId())).findFirst().orElse(null);
		if (node.parentId() != null) {
			var parent = byId.get(node.parentId());
			if (node.viaRelationship() == null && node.viaConnection() == null)
				throw new IllegalStateException(
						"Non-root semantic node " + node.id() + " must specify a relationship or connection.");
			if (node.viaRelationship() != null && node.viaConnection() != null)
				throw new IllegalStateException(
						"Semantic node " + node.id() + " cannot specify both relationship and connection edges.");
			if (node.viaRelationship() != null && parent != null && entity != null)
				validateRelationship(parent.entityId(), node.viaRelationship(), node.entityId(), contract, mode,
						node.id());
		} else if (node.viaRelationship() != null || node.viaConnection() != null)
			throw new IllegalStateException("Root semantic node " + node.id() + " cannot specify a parent edge.");
		if (entity == null) {
			if (mode == SemanticGraphValidationMode.FEDERATED || mode == SemanticGraphValidationMode.EXPLORATORY)
				return;
			throw new IllegalStateException(
					"Semantic node " + node.id() + " references unknown entity '" + node.entityId() + "'.");
		}
		for (var field : new HashSet<>(node.fields()))
			if (!field.equals(entity.identity().fieldId())
					&& entity.fields().stream().noneMatch(f -> f.id().equals(field))) {
				if (mode == SemanticGraphValidationMode.EXPLORATORY)
					continue;
				throw new IllegalStateException("Semantic node " + node.id() + " selects unknown field '" + field
						+ "' on '" + entity.name() + "'.");
			}
	}

	private static void validateRelationship(EntityId parentId, RelationshipId rid, EntityId target,
			SemanticModel model, SemanticGraphValidationMode mode, int nodeId) {
		var p = model.get(parentId);
		var r = p.relationships().stream().filter(x -> x.id().equals(rid)).findFirst().orElse(null);
		if (r == null) {
			if (mode == SemanticGraphValidationMode.FEDERATED || mode == SemanticGraphValidationMode.EXPLORATORY)
				return;
			throw new IllegalStateException("Parent entity '" + p.name() + "' does not declare relationship '" + rid
					+ "' for semantic node " + nodeId + ".");
		}
		if (!r.target().equals(target))
			throw new IllegalStateException("Semantic node " + nodeId + " targets entity '" + target
					+ "', but relationship '" + r.name() + "' targets '" + r.target() + "'.");
	}

	private static void validateRelationship(EntityId parentId, RelationshipId rid, EntityId target,
			SemanticContractSnapshot contract, SemanticGraphValidationMode mode, int nodeId) {
		var p = contract.get(parentId);
		var r = p.relationships().stream().filter(x -> x.id().equals(rid)).findFirst().orElse(null);
		if (r == null) {
			if (mode == SemanticGraphValidationMode.FEDERATED || mode == SemanticGraphValidationMode.EXPLORATORY)
				return;
			throw new IllegalStateException("Parent entity '" + p.name() + "' does not declare relationship '" + rid
					+ "' for semantic node " + nodeId + ".");
		}
		if (!r.target().equals(target))
			throw new IllegalStateException("Semantic node " + nodeId + " targets entity '" + target
					+ "', but relationship '" + r.name() + "' targets '" + r.target() + "'.");
	}
}
