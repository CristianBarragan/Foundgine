package com.foundgine.core.execution;

import com.foundgine.core.semantic.results.SemanticResult;
import com.foundgine.core.semantic.results.SemanticResultEvidence;
import com.foundgine.core.semantic.results.SemanticResultNode;
import com.foundgine.core.semantic.results.SemanticResultPageInfo;

import java.util.List;

/**
 * Compatibility name for the semantic result tree. New code should consume
 * {@link SemanticResult} directly.
 *
 * <p>
 * C# exposes an implicit conversion operator to {@code SemanticResult}; Java
 * has no implicit user-defined conversions, so {@link #toSemanticResult()} is
 * the explicit equivalent callers must invoke.
 */
@Deprecated
public record MaterializedResult(List<SemanticResultNode> roots, SemanticResultPageInfo pageInfo,
		SemanticResultEvidence evidence) {

	public MaterializedResult(List<SemanticResultNode> roots) {
		this(roots, null, null);
	}

	public SemanticResult toSemanticResult() {
		return new SemanticResult(roots, pageInfo, evidence);
	}
}
