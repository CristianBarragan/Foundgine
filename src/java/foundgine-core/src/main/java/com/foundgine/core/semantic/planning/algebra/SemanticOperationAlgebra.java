package com.foundgine.core.semantic.planning.algebra;

import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.ir.graph.SemanticOperationGraph;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Deterministic, provider-neutral algebra over semantic operation graphs. */
public final class SemanticOperationAlgebra {
	private SemanticOperationAlgebra() {
	}

	public static void validate(SemanticOperationGraph graph) {
		Objects.requireNonNull(graph);
		var visited = new HashSet<Integer>();
		visit(graph, graph.rootId(), visited, null);
		if (visited.size() != graph.nodes().size())
			throw new IllegalStateException("Semantic operation graph contains unreachable nodes.");
	}

	public static SemanticOperationGraph where(SemanticOperationGraph graph, SemanticFilterExpression predicate) {
		Objects.requireNonNull(predicate);
		validate(graph);
		var op = graph.toOperation();
		var root = op.root();
		var existing = root.queryOptions() == null ? null : root.queryOptions().filter();
		var combined = existing == null ? predicate : new SemanticAndFilter(List.of(existing, predicate));
		var options = root.queryOptions() == null ? new SemanticQueryOptions() : root.queryOptions();
		var updatedOptions = new SemanticQueryOptions(combined, options.order(), options.limit(), options.offset(),
				options.after());
		return SemanticOperationGraph.create(new SemanticOperation(new SemanticReadNode(root.id(), root.entityId(),
				root.fields(), root.viaRelationship(), root.viaConnection(), root.children(), updatedOptions,
				root.authorization(), root.requiredFields())));
	}

	public static SemanticOperationGraph normalize(SemanticOperationGraph graph) {
		Objects.requireNonNull(graph);
		validate(graph);
		var root = normalizeNode(graph.toOperation().root());
		return SemanticOperationGraph.create(new SemanticOperation(root));
	}

	private static SemanticReadNode normalizeNode(SemanticReadNode node) {
		var children = node.children().stream().map(SemanticOperationAlgebra::normalizeNode).toList();
		var fields = distinct(node.fields());
		var required = distinct(node.requiredFields());
		return new SemanticReadNode(node.id(), node.entityId(), fields, node.viaRelationship(), node.viaConnection(),
				children, node.queryOptions(), node.authorization(), required);
	}

	private static <T> List<T> distinct(List<T> values) {
		return new ArrayList<>(new LinkedHashSet<>(values));
	}

	private static void visit(SemanticOperationGraph graph, int id, Set<Integer> visited, Integer parent) {
		if (!visited.add(id))
			throw new IllegalStateException(
					"Semantic operation graph contains a cycle or duplicate reference at node '" + id + "'.");
		var node = graph.getNode(id);
		if (!Objects.equals(node.parentId(), parent))
			throw new IllegalStateException("Semantic operation graph parent mismatch for node '" + id + "'.");
		if (node.isRoot() && (node.viaRelationship() != null || node.viaConnection() != null))
			throw new IllegalStateException("The graph root cannot specify a parent edge.");
		if (!node.isRoot() && node.viaRelationship() == null && node.viaConnection() == null)
			throw new IllegalStateException(
					"Non-root node '" + id + "' must specify a relationship or connection edge.");
		if (node.viaRelationship() != null && node.viaConnection() != null)
			throw new IllegalStateException(
					"Node '" + id + "' cannot specify both a relationship and connection edge.");
		for (var child : node.children())
			visit(graph, child, visited, id);
	}
}
