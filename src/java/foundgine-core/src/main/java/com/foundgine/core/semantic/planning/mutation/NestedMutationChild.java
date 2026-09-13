package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.RelationshipId;
import java.util.*;

public record NestedMutationChild(RelationshipId relationship, NestedMutationIntent mutation) {
	public NestedMutationChild {
		Objects.requireNonNull(relationship);
		Objects.requireNonNull(mutation);
	}
}
