package com.foundgine.core.execution.mutation;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.MutationMaterializedResult}.
 *
 * <p>Semantic result of a mutation tree. The tree mirrors the nested
 * mutation intent rather than the provider's flat operation list.
 */
public record MutationMaterializedResult(List<MutationMaterializedNode> roots) {

    public static final MutationMaterializedResult EMPTY = new MutationMaterializedResult(List.of());
}
