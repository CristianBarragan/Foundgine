package com.foundgine.core.semantic.ir.graph;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;

/**
 * Immutable provider-neutral operation graph derived from canonical Semantic
 * IR.
 */
public final class SemanticOperationGraph {
	private final Map<Integer, SemanticOperationGraphNode> nodes;
	private final int rootId;

	private SemanticOperationGraph(Map<Integer, SemanticOperationGraphNode> nodes, int rootId) {
		this.nodes = Map.copyOf(nodes);
		this.rootId = rootId;
	}

	public int rootId() {
		return rootId;
	}

	public Collection<SemanticOperationGraphNode> nodes() {
		return nodes.values();
	}

	public SemanticOperationGraphNode root() {
		return getNode(rootId);
	}

	public SemanticOperationGraphNode getNode(int id) {
		var n = nodes.get(id);
		if (n == null)
			throw new NoSuchElementException("Semantic operation graph does not contain node '" + id + "'.");
		return n;
	}

	public String fingerprint() {
		return SemanticOperationGraphFingerprint.create(this);
	}

	public static SemanticOperationGraph create(SemanticOperation operation) {
		Objects.requireNonNull(operation);
		var map = new LinkedHashMap<Integer, SemanticOperationGraphNode>();
		build(operation.root(), map, null, true);
		return new SemanticOperationGraph(map, operation.root().id());
	}

	public SemanticOperation toOperation() {
		return new SemanticOperation(buildOperationNode(rootId));
	}

	private SemanticReadNode buildOperationNode(int id) {
		var n = getNode(id);
		var children = n.children().stream().map(this::buildOperationNode).toList();
		return new SemanticReadNode(n.id(), n.entityId(), n.fields(), n.viaRelationship(), n.viaConnection(), children,
				n.queryOptions(), n.authorization(), n.requiredFields());
	}

	private static void build(SemanticReadNode n, Map<Integer, SemanticOperationGraphNode> map, Integer parent,
			boolean root) {
		if (map.putIfAbsent(n.id(), new SemanticOperationGraphNode(n.id(), n.entityId(), n.fields(), n.requiredFields(),
				n.viaRelationship(), n.viaConnection(), n.children().stream().map(SemanticReadNode::id).toList(),
				parent, root ? n.queryOptions() : null, n.authorization())) != null)
			throw new IllegalArgumentException(
					"Semantic operation graph contains a duplicate node id '" + n.id() + "'.");
		if (!root && n.viaRelationship() == null && n.viaConnection() == null)
			throw new IllegalArgumentException("Non-root semantic node " + n.id()
					+ " must specify the relationship or connection used to reach it.");
		if (n.viaRelationship() != null && n.viaConnection() != null)
			throw new IllegalArgumentException(
					"Semantic node " + n.id() + " cannot specify both a relationship and a connection.");
		if (root && (n.viaRelationship() != null || n.viaConnection() != null))
			throw new IllegalArgumentException("Root semantic node " + n.id() + " cannot specify a parent edge.");
		for (var c : n.children())
			build(c, map, n.id(), false);
	}
}
