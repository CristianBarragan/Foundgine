package com.foundgine.core.execution.mutation;

import com.foundgine.core.semantic.planning.mutation.MutationBatchPlan;
import com.foundgine.core.semantic.planning.mutation.MutationDependency;
import com.foundgine.core.semantic.planning.mutation.MutationOperation;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.ExecutionMutationIR} and its
 * companion {@code ExecutionMutationIRCompiler}.
 *
 * <p>Canonical provider-neutral execution representation for mutation work.
 *
 * <p>Mutation semantics are resolved before this boundary. This representation
 * contains the concrete provider-neutral work required to execute a mutation
 * batch, including dependency edges, but contains no SQL or provider-specific
 * plan types.
 *
 * <p>C#'s {@code RequiredSecurityInvariants} is an {@code init}-only property
 * defaulting to an empty array; ported here as a third canonical-constructor
 * component (defaulted to empty by the two-argument constructor and by
 * {@link #from(MutationBatchPlan)}), mirroring every construction path this
 * codebase actually exercises.
 */
public record ExecutionMutationIR(
        List<MutationOperation> operations,
        List<MutationDependency> dependencies,
        List<String> requiredSecurityInvariants) {

    public ExecutionMutationIR {
        Objects.requireNonNull(operations);
        Objects.requireNonNull(dependencies);
        operations = List.copyOf(operations);
        dependencies = List.copyOf(dependencies);
        requiredSecurityInvariants = requiredSecurityInvariants == null ? List.of() : List.copyOf(requiredSecurityInvariants);
    }

    public ExecutionMutationIR(List<MutationOperation> operations, List<MutationDependency> dependencies) {
        this(operations, dependencies, List.of());
    }

    public static ExecutionMutationIR from(MutationBatchPlan plan) {
        return from(plan, null);
    }

    public static ExecutionMutationIR from(MutationBatchPlan plan, List<String> requiredSecurityInvariants) {
        Objects.requireNonNull(plan);

        if (plan.operations().isEmpty())
            throw new IllegalStateException("An execution mutation IR must contain at least one operation.");

        validateDependencies(plan.operations().size(), plan.dependencies());

        List<String> normalized = requiredSecurityInvariants == null
                ? List.of()
                : new LinkedHashSet<>(requiredSecurityInvariants).stream().sorted().toList();

        return new ExecutionMutationIR(plan.operations(), plan.dependencies(), normalized);
    }

    /**
     * Materializes the canonical provider-neutral mutation batch consumed by
     * existing mutation compilers. No correlation-specific representation is
     * introduced here.
     */
    public MutationBatchPlan toMutationBatchPlan() {
        return new MutationBatchPlan(operations, dependencies);
    }

    /**
     * Derives dependency edges from field-level value references. This is a
     * validation/consistency operation; {@link #dependencies()} remains the
     * canonical execution graph input.
     */
    public List<MutationDependency> deriveDependencies() {
        List<MutationDependency> result = new ArrayList<>();

        for (int targetIndex = 0; targetIndex < operations.size(); targetIndex++) {
            for (var field : operations.get(targetIndex).fields()) {
                var source = field.source();
                if (source == null)
                    continue;

                if (source.sourceOperationIndex() < 0 || source.sourceOperationIndex() >= operations.size()) {
                    throw new IllegalStateException(
                            "Mutation value reference points to invalid source operation "
                                    + source.sourceOperationIndex() + ".");
                }

                if (source.sourceOperationIndex() >= targetIndex) {
                    throw new IllegalStateException(
                            "Mutation value reference must point from an earlier operation: "
                                    + source.sourceOperationIndex() + " -> " + targetIndex + ".");
                }

                result.add(new MutationDependency(
                        source.sourceOperationIndex(), targetIndex, source.sourceField(), field.column()));
            }
        }

        return result;
    }

    /**
     * Ensures canonical dependency metadata agrees with dependency edges
     * derivable from field-level value references.
     */
    public void validateDerivedDependencies() {
        Comparator<MutationDependency> order = Comparator
                .comparingInt(MutationDependency::sourceOperationIndex)
                .thenComparingInt(MutationDependency::targetOperationIndex)
                .thenComparingLong(d -> d.sourceField().value())
                .thenComparingLong(d -> d.targetColumn().value());

        List<MutationDependency> expected = deriveDependencies().stream().sorted(order).toList();
        List<MutationDependency> actual = dependencies.stream().sorted(order).toList();

        if (!expected.equals(actual)) {
            throw new IllegalStateException(
                    "Mutation dependency metadata disagrees with field correlation references.");
        }
    }

    private static void validateDependencies(int operationCount, List<MutationDependency> dependencies) {
        for (MutationDependency dependency : dependencies) {
            if (dependency.sourceOperationIndex() < 0 || dependency.sourceOperationIndex() >= operationCount
                    || dependency.targetOperationIndex() < 0 || dependency.targetOperationIndex() >= operationCount) {
                throw new IllegalStateException(
                        "Mutation dependency indexes are outside the execution IR: "
                                + dependency.sourceOperationIndex() + " -> " + dependency.targetOperationIndex() + ".");
            }

            if (dependency.sourceOperationIndex() >= dependency.targetOperationIndex()) {
                throw new IllegalStateException(
                        "Mutation dependency must point from an earlier operation: "
                                + dependency.sourceOperationIndex() + " -> " + dependency.targetOperationIndex() + ".");
            }
        }
    }

    /**
     * Port of {@code Foundgine.Core.Execution.Mutation.ExecutionMutationIRCompiler}:
     * explicit lowering from the provider-neutral mutation planning artifact to
     * the canonical execution representation.
     */
    public static final class ExecutionMutationIRCompiler {
        private ExecutionMutationIRCompiler() {
        }

        public static ExecutionMutationIR compile(MutationBatchPlan plan) {
            return ExecutionMutationIR.from(plan);
        }
    }
}
