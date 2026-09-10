package com.foundgine.core.execution.mutation;

import com.foundgine.core.semantic.planning.mutation.MutationDependency;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;

/**
 * Canonical provider-neutral execution dependency graph.
 * Dependency edges are already resolved to operation ordinals and physical
 * target columns. Provider-specific correlation carriers are introduced only
 * during provider lowering.
 */
public final class MutationDependencyGraph {

    /**
     * C# represents an edge as the value-tuple {@code (int SourceOperationIndex, int TargetOperationIndex)};
     * Java has no built-in tuple type, so this is ported as a small nested record.
     */
    public record Edge(int sourceOperationIndex, int targetOperationIndex) {
    }

    private final List<MutationDependency> dependencies;

    public MutationDependencyGraph(List<MutationDependency> dependencies) {
        Objects.requireNonNull(dependencies);
        this.dependencies = List.copyOf(dependencies);
    }

    public List<MutationDependency> dependencies() {
        return dependencies;
    }

    public Set<Edge> edges() {
        Set<Edge> edges = new LinkedHashSet<>();
        for (MutationDependency d : dependencies)
            edges.add(new Edge(d.sourceOperationIndex(), d.targetOperationIndex()));
        return edges;
    }
}
