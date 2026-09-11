package com.foundgine.core.semantic.ir.graph;

import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;

import java.util.HashSet;
import java.util.Objects;
import java.util.Set;

/**
 * Port of {@code Foundgine.Core.Semantic.IR.Graph.SemanticOperationGraphSafetyValidator}.
 *
 * <p>Enforces bounded graph shape after dynamic intent has been resolved
 * into canonical semantic operations. This is intentionally
 * provider-neutral and protects callers that construct operation graphs
 * without going through the JSON/MCP adapters.
 *
 * <p><b>Porting decision:</b> the C# implementation uses a local function
 * ({@code Walk}) that closes over mutable local counters ({@code edges},
 * {@code fields}, {@code maxDepth}, {@code visited}) by reference. Java
 * lambdas/local classes cannot mutate captured local primitives, so the
 * walk is ported as a private recursive method on a small mutable
 * {@link WalkState} holder that plays the same role as the captured
 * locals.
 */
public final class SemanticOperationGraphSafetyValidator {
    private SemanticOperationGraphSafetyValidator() {
    }

    public static void validate(SemanticOperationGraph graph, SecurityResourceLimits limits) {
        Objects.requireNonNull(graph, "graph");
        Objects.requireNonNull(limits, "limits");
        limits.validate();

        var nodes = graph.nodes().size();
        if (nodes > limits.maxOperationGraphNodes()) {
            reject("Semantic operation graph exceeds the configured maximum of "
                    + limits.maxOperationGraphNodes() + " nodes.");
        }

        var state = new WalkState();
        walk(graph, graph.root(), 1, limits, state);

        if (state.edges > limits.maxOperationGraphEdges()) {
            reject("Semantic operation graph exceeds the configured maximum of "
                    + limits.maxOperationGraphEdges() + " edges.");
        }
        if (state.fields > limits.maxOperationGraphFields()) {
            reject("Semantic operation graph exceeds the configured maximum of "
                    + limits.maxOperationGraphFields() + " fields.");
        }
        if (state.visited.size() != nodes) {
            reject("Semantic operation graph contains unreachable nodes.");
        }
    }

    private static void walk(
            SemanticOperationGraph graph,
            SemanticOperationGraphNode node,
            int depth,
            SecurityResourceLimits limits,
            WalkState state) {
        if (!state.visited.add(node.id())) {
            reject("Semantic operation graph contains a repeated node '" + node.id() + "' or cycle.");
        }

        if (depth > limits.maxOperationGraphDepth()) {
            reject("Semantic operation graph depth exceeds the configured maximum of "
                    + limits.maxOperationGraphDepth() + " levels.");
        }

        state.maxDepth = Math.max(state.maxDepth, depth);
        state.fields += node.fields().size() + node.requiredFields().size();
        if (state.fields > limits.maxOperationGraphFields()) {
            reject("Semantic operation graph exceeds the configured maximum of "
                    + limits.maxOperationGraphFields() + " fields.");
        }

        for (var childId : node.children()) {
            state.edges++;
            if (state.edges > limits.maxOperationGraphEdges()) {
                reject("Semantic operation graph exceeds the configured maximum of "
                        + limits.maxOperationGraphEdges() + " edges.");
            }

            var child = graph.getNode(childId);
            if (!java.util.Objects.equals(child.parentId(), node.id())) {
                reject("Semantic operation graph edge '" + node.id() + "->" + childId
                        + "' has an inconsistent parent reference.");
            }
            walk(graph, child, depth + 1, limits, state);
        }
    }

    private static void reject(String message) {
        throw new IllegalStateException(message);
    }

    private static final class WalkState {
        int edges;
        int fields;
        int maxDepth;
        final Set<Integer> visited = new HashSet<>();
    }
}
