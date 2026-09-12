package com.foundgine.core.semantic.resolution;

import java.util.List;

public interface ISemanticLexicalCandidateSource {
	List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request);

	default List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request,
			CancellationToken cancellationToken) {
		return retrieve(request);
	}

	interface CancellationToken {
		boolean isCancellationRequested();
	}
}
