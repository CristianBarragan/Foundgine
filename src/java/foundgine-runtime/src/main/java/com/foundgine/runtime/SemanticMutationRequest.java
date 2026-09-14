package com.foundgine.runtime;

import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

import java.util.Objects;

/**
 * Port of the {@code SemanticMutationRequest} record declared alongside
 * {@code IFoundgineMutations}.
 */
public record SemanticMutationRequest(SemanticMutationOperationGraph graph, SecurityExecutionContext security) {

	public SemanticMutationRequest {
		Objects.requireNonNull(graph, "graph");
	}

	public SemanticMutationRequest(SemanticMutationOperationGraph graph) {
		this(graph, null);
	}
}
