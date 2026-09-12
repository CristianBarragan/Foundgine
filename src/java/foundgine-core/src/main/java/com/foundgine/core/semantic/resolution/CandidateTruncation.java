package com.foundgine.core.semantic.resolution;

public record CandidateTruncation(String token, int retainedCount, int truncatedCount, double lowestRetainedScore,
		double highestTruncatedScore) {
	public double marginGap() {
		return lowestRetainedScore - highestTruncatedScore;
	}

	public boolean withinAmbiguityMargin(double ambiguityThreshold) {
		return marginGap() < ambiguityThreshold;
	}
}
