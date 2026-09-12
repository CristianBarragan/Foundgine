package com.foundgine.core.semantic.resolution;

import java.util.List;

public record SemanticLexicalStep(String token, SemanticLexicalCandidate candidate, double pathScore,
		List<SemanticLexicalCandidate> bridgingPath) {
	public SemanticLexicalStep {
		bridgingPath = bridgingPath == null ? List.of() : List.copyOf(bridgingPath);
	}
}
