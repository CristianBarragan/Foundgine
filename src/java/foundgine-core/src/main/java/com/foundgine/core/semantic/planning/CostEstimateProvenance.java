package com.foundgine.core.semantic.planning;

import java.time.*;

/**
 * Describes where a provider cost estimate came from and how current its
 * statistics are.
 */
public record CostEstimateProvenance(String source, String statisticsVersion, Instant estimatedAtUtc,
		Duration statisticsAge, CostStatisticsFreshness freshness) {
	public static CostEstimateProvenance heuristic() {
		return heuristic("heuristic", Instant.now());
	}

	public static CostEstimateProvenance heuristic(String source, Instant estimatedAtUtc) {
		return new CostEstimateProvenance(source, null, estimatedAtUtc, null, CostStatisticsFreshness.UNKNOWN);
	}

	public static CostEstimateProvenance fromStatistics(String source, String statisticsVersion, Instant observedAtUtc,
			Instant estimatedAtUtc, Duration staleAfter) {
		if (source == null || source.isBlank())
			throw new IllegalArgumentException("Source is required.");
		if (statisticsVersion == null || statisticsVersion.isBlank())
			throw new IllegalArgumentException("Statistics version is required.");
		Instant now = estimatedAtUtc == null ? Instant.now() : estimatedAtUtc;
		if (observedAtUtc.isAfter(now))
			throw new IllegalArgumentException("Observed time cannot be after estimate time.");
		Duration age = Duration.between(observedAtUtc, now);
		CostStatisticsFreshness f = staleAfter == null ? CostStatisticsFreshness.UNKNOWN
				: age.compareTo(staleAfter.dividedBy(2)) <= 0 ? CostStatisticsFreshness.FRESH
						: age.compareTo(staleAfter) <= 0 ? CostStatisticsFreshness.AGING
								: CostStatisticsFreshness.STALE;
		return new CostEstimateProvenance(source, statisticsVersion, now, age, f);
	}
}
