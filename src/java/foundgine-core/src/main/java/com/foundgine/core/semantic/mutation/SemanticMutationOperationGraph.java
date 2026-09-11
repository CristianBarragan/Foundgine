package com.foundgine.core.semantic.mutation;

import java.util.*;

/** Canonical semantic mutation graph. */
public record SemanticMutationOperationGraph(List<SemanticMutationOperation> operations) {
    public SemanticMutationOperationGraph {
        if (operations == null || operations.isEmpty()) throw new IllegalArgumentException("A semantic mutation graph must contain at least one operation.");
        operations = List.copyOf(operations);
    }
    public List<SemanticMutationEffect> effects() { return operations.stream().flatMap(x -> x.effects().stream()).toList(); }
}
