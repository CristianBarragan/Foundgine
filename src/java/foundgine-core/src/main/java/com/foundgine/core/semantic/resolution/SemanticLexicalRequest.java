package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import java.util.List;

public record SemanticLexicalRequest(String token, EntityId contextEntity, List<SemanticLexicalCandidateKind> kinds,
		int limit) {
	public SemanticLexicalRequest {
		if (token == null || token.isBlank())
			throw new IllegalArgumentException("Lexical token cannot be empty.");
		if (limit < 1)
			throw new IllegalArgumentException("Lexical limit must be positive.");
		kinds = kinds == null ? null : List.copyOf(kinds);
	}

	public SemanticLexicalRequest(String token) {
		this(token, null, null, 20);
	}

	public SemanticLexicalRequest(String token, EntityId contextEntity) {
		this(token, contextEntity, null, 20);
	}

	public List<SemanticLexicalCandidateKind> effectiveKinds() {
		return kinds == null ? List.of(SemanticLexicalCandidateKind.values()) : kinds;
	}
}
