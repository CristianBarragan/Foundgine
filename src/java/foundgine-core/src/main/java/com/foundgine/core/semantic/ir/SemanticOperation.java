package com.foundgine.core.semantic.ir;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;

/**
 * Canonical provider-neutral representation of one resolved semantic operation.
 */
public record SemanticOperation(SemanticReadNode root) {
	public SemanticOperation {
		Objects.requireNonNull(root);
	}

	public boolean isReadOnly() {
		return true;
	}
}

/**
 * Semantic read node: domain traversal and selected fields, never provider
 * instructions.
 */
record SemanticReadNodeData() {
}
