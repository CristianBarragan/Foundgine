package com.foundgine.extensions.graphql;

import com.foundgine.core.semantic.planning.mutation.NestedMutationIntent;

/** Port of C# {@code GraphQLMutationAdaptation}: the nested intent plus how to shape its result. */
public record GraphQLMutationAdaptation(NestedMutationIntent intent, GraphQLMutationResultShape resultShape) {
    /** C# {@code Result} alias of {@code ResultShape}. */
    public GraphQLMutationResultShape result() {
        return resultShape;
    }
}
