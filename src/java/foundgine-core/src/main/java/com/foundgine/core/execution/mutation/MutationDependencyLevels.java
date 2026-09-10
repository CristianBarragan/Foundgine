package com.foundgine.core.execution.mutation;

import com.foundgine.core.semantic.planning.mutation.MutationDependency;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.MutationDependencyLevels}.
 *
 * <p>Computes execution levels directly from canonical mutation dependencies.
 * No correlation-shaped compatibility model is involved.
 */
public final class MutationDependencyLevels {

    private MutationDependencyLevels() {
    }

    public static List<List<Integer>> compute(int operationCount, Iterable<MutationDependency> dependencies) {
        if (operationCount < 0)
            throw new IllegalArgumentException("operationCount must not be negative.");
        Objects.requireNonNull(dependencies);

        List<MutationDependency> edges = new ArrayList<>();
        dependencies.forEach(edges::add);

        for (MutationDependency dependency : edges) {
            if (dependency.sourceOperationIndex() < 0 || dependency.sourceOperationIndex() >= operationCount
                    || dependency.targetOperationIndex() < 0 || dependency.targetOperationIndex() >= operationCount) {
                throw new IllegalStateException(
                        "Mutation dependency indexes are outside the execution graph: "
                                + dependency.sourceOperationIndex() + " -> " + dependency.targetOperationIndex() + ".");
            }

            if (dependency.sourceOperationIndex() == dependency.targetOperationIndex()) {
                throw new IllegalStateException(
                        "Mutation dependency cycle detected at operation " + dependency.sourceOperationIndex() + ".");
            }
        }

        if (operationCount == 0)
            return List.of();

        Map<Integer, Integer> incoming = new HashMap<>();
        Map<Integer, List<Integer>> outgoing = new HashMap<>();
        for (int i = 0; i < operationCount; i++) {
            incoming.put(i, 0);
            outgoing.put(i, new ArrayList<>());
        }

        for (MutationDependency dependency : edges) {
            List<Integer> targets = outgoing.get(dependency.sourceOperationIndex());
            if (!targets.contains(dependency.targetOperationIndex())) {
                targets.add(dependency.targetOperationIndex());
                incoming.merge(dependency.targetOperationIndex(), 1, Integer::sum);
            }
        }

        Set<Integer> remaining = new HashSet<>();
        for (int i = 0; i < operationCount; i++)
            remaining.add(i);

        List<List<Integer>> levels = new ArrayList<>();

        while (!remaining.isEmpty()) {
            List<Integer> level = remaining.stream()
                    .filter(i -> incoming.get(i) == 0)
                    .sorted()
                    .toList();

            if (level.isEmpty())
                throw new IllegalStateException("Mutation dependency graph contains a cycle.");

            levels.add(level);

            for (int node : level) {
                remaining.remove(node);
                for (int target : outgoing.get(node))
                    incoming.merge(target, -1, Integer::sum);
            }
        }

        return levels;
    }
}
