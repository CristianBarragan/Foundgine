package com.foundgine.core.semantic.ir;

import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;
import java.util.function.*;
import java.util.stream.*;

/** Lowers a resolved SemanticGraph into canonical provider-neutral Semantic IR. */
public final class SemanticOperationCompiler {
    private SemanticOperationCompiler() {}
    public static SemanticOperation compile(SemanticGraph graph) {
        Objects.requireNonNull(graph);
        if (graph.nodes().isEmpty()) throw new IllegalArgumentException("Cannot compile an empty semantic graph.");
        var roots = graph.nodes().stream().filter(n -> n.parentId() == null).toList();
        if (roots.size() != 1) throw new IllegalArgumentException("A semantic operation requires exactly one root semantic node.");
        var ids = graph.nodes().stream().map(SemanticGraph.SemanticGraphNode::id).collect(Collectors.toSet());
        var visited = new HashSet<Integer>();
        var root = build(roots.get(0), graph.nodes(), ids, visited, graph.options(), true);
        if (visited.size() != graph.nodes().size()) throw new IllegalArgumentException("Semantic graph contains unreachable nodes.");
        return new SemanticOperation(root);
    }
    private static SemanticReadNode build(SemanticGraph.SemanticGraphNode n, List<SemanticGraph.SemanticGraphNode> all,
                                          Set<Integer> ids, Set<Integer> visited, SemanticQueryOptions options, boolean root) {
        if (!visited.add(n.id())) throw new IllegalArgumentException("Semantic graph contains a cycle at node " + n.id());
        if (n.parentId() != null && !ids.contains(n.parentId())) throw new IllegalArgumentException("Semantic node " + n.id() + " references missing parent node " + n.parentId());
        if (!root && n.viaRelationship() == null && n.viaConnection() == null) throw new IllegalArgumentException("Non-root semantic node " + n.id() + " must specify a relationship or connection.");
        if (root && (n.viaRelationship() != null || n.viaConnection() != null)) throw new IllegalArgumentException("Root semantic node " + n.id() + " cannot specify a parent edge.");
        var children = all.stream().filter(x -> Objects.equals(x.parentId(), n.id())).map(x -> build(x, all, ids, visited, null, false)).toList();
        return new SemanticReadNode(n.id(), n.entityId(), n.fields(), n.viaRelationship(), n.viaConnection(), children,
                root ? options : null, n.authorization());
    }
}
