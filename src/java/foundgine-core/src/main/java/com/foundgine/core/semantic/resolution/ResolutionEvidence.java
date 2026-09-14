package com.foundgine.core.semantic.resolution;

public record ResolutionEvidence(String description, CandidateEvidenceKind kind, Double score) {
	public ResolutionEvidence(String description) {
		this(description, null, null);
	}

	public ResolutionEvidence(String description, CandidateEvidenceKind kind) {
		this(description, kind, null);
	}

	public ResolutionEvidence(String description, CandidateEvidenceKind kind, double score) {
		this(description, kind, Double.valueOf(score));
	}
}
