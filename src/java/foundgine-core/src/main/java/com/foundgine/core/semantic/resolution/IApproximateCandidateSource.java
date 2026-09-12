package com.foundgine.core.semantic.resolution;

import java.util.List;

public interface IApproximateCandidateSource {
	List<RetrievalCandidate> retrieve(SemanticRetrievalRequest request);
}
