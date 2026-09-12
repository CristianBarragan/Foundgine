package com.foundgine.core.semantic.intent;

import com.foundgine.core.semantic.query.*;
import java.util.*;

public record ReadOrder(String field, SemanticSortDirection direction, List<String> relationshipPath,
		SemanticOrderAggregate aggregate) {
	public ReadOrder {
		relationshipPath = relationshipPath == null ? List.of() : List.copyOf(relationshipPath);
		aggregate = aggregate == null ? SemanticOrderAggregate.NONE : aggregate;
	}

	public ReadOrder(String field, SemanticSortDirection direction) {
		this(field, direction, List.of(), SemanticOrderAggregate.NONE);
	}

	public List<String> effectivePath() {
		return relationshipPath;
	}
}
