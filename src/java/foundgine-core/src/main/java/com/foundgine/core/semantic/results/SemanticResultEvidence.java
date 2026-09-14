package com.foundgine.core.semantic.results;

import java.util.List;

/** Optional execution provenance without provider/transport objects. */
public record SemanticResultEvidence(String provider, String planFingerprint, List<Integer> authorizedNodeIds,
		int rowsReturned, long elapsedMilliseconds, String providerOperationFingerprint, String intentFingerprint,
		String authorizationFingerprint) {
	public SemanticResultEvidence {
		authorizedNodeIds = authorizedNodeIds == null ? List.of() : List.copyOf(authorizedNodeIds);
	}
}
