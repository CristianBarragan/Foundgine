package com.foundgine.core.semantic.planning;

import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

/** Port of {@code CostEstimateProvenanceTests} (Foundgine.Planning.Tests). */
class CostEstimateProvenanceParityTest {

	@Test
	void heuristicEstimateHasUnknownStatisticsFreshness() {
		var estimate = ProviderCostEstimate.from("test", 10d);

		assertEquals("heuristic", estimate.effectiveProvenance().source());
		assertEquals(CostStatisticsFreshness.UNKNOWN, estimate.effectiveProvenance().freshness());
		assertNull(estimate.effectiveProvenance().statisticsVersion());
	}

	@Test
	void statisticsProvenanceRecordsVersionAgeAndFreshness() {
		var observed = Instant.parse("2026-08-16T10:00:00Z");
		var estimated = observed.plus(Duration.ofMinutes(5));
		var provenance = CostEstimateProvenance.fromStatistics("postgresql.analyze", "stats-42", observed, estimated,
				Duration.ofHours(1));

		assertEquals("stats-42", provenance.statisticsVersion());
		assertEquals(Duration.ofMinutes(5), provenance.statisticsAge());
		assertEquals(CostStatisticsFreshness.FRESH, provenance.freshness());
	}

	@Test
	void staleStatisticsAreExplicitlyMarked() {
		var observed = Instant.parse("2026-08-15T10:00:00Z");
		var estimated = observed.plus(Duration.ofHours(25));
		var provenance = CostEstimateProvenance.fromStatistics("postgresql.analyze", "stats-old", observed, estimated,
				Duration.ofHours(24));

		assertEquals(CostStatisticsFreshness.STALE, provenance.freshness());
		assertEquals(Duration.ofHours(25), provenance.statisticsAge());
	}

	@Test
	void futureStatisticsObservationIsRejected() {
		var observed = Instant.parse("2026-08-16T11:00:00Z");
		var estimated = observed.minus(Duration.ofMinutes(1));

		assertThrows(IllegalArgumentException.class, () -> CostEstimateProvenance.fromStatistics("postgresql.analyze",
				"stats", observed, estimated, Duration.ofHours(1)));
	}
}
