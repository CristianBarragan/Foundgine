package com.foundgine.core.semantic.planning;

public record ProviderAwareRewriteRuleCandidate(String ruleName, String provider, double benefitEstimate,
		double rewriteCost, double estimatedExecutionCost, double estimatedRows, double costConfidence, double score,
		int priority) {
}
