package com.foundgine.core.semantic.security.execution;

/**
 * Bounds applied to the protocol-neutral semantic request before resolution and
 * planning. These limits protect the semantic engine even when an adapter
 * other than JSON is used.
 *
 * <p>Provided as the full canonical constructor (all 18 values) plus a no-arg
 * convenience constructor carrying sensible defaults for
 * callers that want the defaults untouched. A caller that needs to override
 * a subset must go through the canonical constructor with the remaining
 * values copied from {@link #defaults()}.
 */
public record SecurityResourceLimits(
        int maxSelectionDepth,
        int maxOperationGraphNodes,
        int maxOperationGraphDepth,
        int maxOperationGraphEdges,
        int maxOperationGraphFields,
        int maxSelectionNodes,
        int maxFilterDepth,
        int maxFilterNodes,
        int maxOrderTerms,
        int maxOrderPathDepth,
        int maxPageSize,
        int maxOffset,
        int maxCursorLength,
        int maxMutationOperations,
        int maxMutationFieldsPerOperation,
        int maxMutationReturnFieldsPerOperation,
        int maxMutationDependencies,
        int maxMutationEffects) {

    public SecurityResourceLimits() {
        this(32, 256, 32, 255, 512, 256, 32, 256, 64, 16, 1000, 1_000_000, 4096, 128, 64, 64, 256, 256);
    }

    /** The default limit set, equivalent to {@code new SecurityResourceLimits()}. */
    public static SecurityResourceLimits defaults() {
        return new SecurityResourceLimits();
    }

    public void validate() {
        if (maxSelectionDepth < 1 || maxOperationGraphNodes < 1 || maxOperationGraphDepth < 1
                || maxOperationGraphEdges < 1 || maxOperationGraphFields < 1 || maxSelectionNodes < 1
                || maxFilterDepth < 1 || maxFilterNodes < 1
                || maxOrderTerms < 1 || maxOrderPathDepth < 1
                || maxPageSize < 1 || maxOffset < 0 || maxCursorLength < 1
                || maxMutationOperations < 1 || maxMutationFieldsPerOperation < 1
                || maxMutationReturnFieldsPerOperation < 1 || maxMutationDependencies < 1
                || maxMutationEffects < 1) {
            throw new IllegalArgumentException(
                    "All security resource limits must be positive, except maxOffset which may be zero.");
        }
    }
}