package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.util.*;

public record SemanticTraversal(EntityId source, String name, EntityId target, List<RelationshipId> path) {
	public SemanticTraversal {
		Objects.requireNonNull(source);
		Objects.requireNonNull(name);
		Objects.requireNonNull(target);
		path = List.copyOf(path);
		if (path.isEmpty())
			throw new IllegalArgumentException("A semantic traversal must contain at least one relationship.");
	}

	public RelationshipId firstRelationship() {
		return path.get(0);
	}
}
