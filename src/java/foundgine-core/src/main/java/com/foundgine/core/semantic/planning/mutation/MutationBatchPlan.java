package com.foundgine.core.semantic.planning.mutation;

import java.util.*;

public record MutationBatchPlan(List<MutationOperation> operations, List<MutationDependency> dependencies) {
	public MutationBatchPlan {
		operations = List.copyOf(operations);
		dependencies = List.copyOf(dependencies);
	}
}
