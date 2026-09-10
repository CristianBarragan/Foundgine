package com.foundgine.core.execution.mutation;

import com.foundgine.core.semantic.planning.mutation.MutationDependency;

import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.MutationExecutionLevels}.
 *
 * <p>Provider-facing immutable dependency levels derived from the canonical
 * execution dependency graph.
 */
public record MutationExecutionLevels(List<List<Integer>> levels) {

    public MutationExecutionLevels {
        levels = List.copyOf(levels);
    }

    public static MutationExecutionLevels from(int operationCount, Iterable<MutationDependency> dependencies) {
        return new MutationExecutionLevels(MutationDependencyLevels.compute(operationCount, dependencies));
    }

    public static MutationExecutionLevels from(ExecutionMutationIR ir) {
        Objects.requireNonNull(ir);
        return from(ir.operations().size(), ir.dependencies());
    }
}
