package com.foundgine.providers.storage.sql;

import com.foundgine.core.semantic.metadata.IMetadataProvider;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import java.time.*;

/** Conservative provider-aware cost model for SQL plan selection. */
public record SqlCostModelOptions(double scanBaseCost, double fieldCost, double traverseCost, double filterCost,
		double relationshipFilterCost, double aggregateFilterCost, double orderTermCost, double limitAdjustment,
		double offsetCost, double cursorAdjustment, double traversalOrderDiscount, String statisticsSource,
		String statisticsVersion, Instant statisticsObservedAtUtc, Duration statisticsStaleAfter) {
	public SqlCostModelOptions() {
		this(10d, .25d, 3d, 1.5d, 4d, 5d, 1.25d, .5d, 2d, -1d, .75d, "heuristic", null, null, null);
	}

	public SqlCostModelOptions validate() {
		validateNonNegative(scanBaseCost, "scanBaseCost");
		validateNonNegative(fieldCost, "fieldCost");
		validateNonNegative(traverseCost, "traverseCost");
		validateNonNegative(filterCost, "filterCost");
		validateNonNegative(relationshipFilterCost, "relationshipFilterCost");
		validateNonNegative(aggregateFilterCost, "aggregateFilterCost");
		validateNonNegative(orderTermCost, "orderTermCost");
		validateNonNegative(limitAdjustment, "limitAdjustment");
		validateNonNegative(offsetCost, "offsetCost");
		if (Double.isNaN(cursorAdjustment) || Double.isInfinite(cursorAdjustment))
			throw new IllegalArgumentException("cursorAdjustment");
		validateNonNegative(traversalOrderDiscount, "traversalOrderDiscount");
		if (statisticsSource == null || statisticsSource.isBlank())
			throw new IllegalArgumentException("Statistics source is required.");
		if (statisticsObservedAtUtc != null && statisticsVersion == null)
			throw new IllegalArgumentException(
					"Statistics version is required when an observation timestamp is supplied.");
		if (statisticsStaleAfter != null && statisticsStaleAfter.isNegative())
			throw new IllegalArgumentException("statisticsStaleAfter");
		return this;
	}

	private static void validateNonNegative(double v, String n) {
		if (Double.isNaN(v) || Double.isInfinite(v) || v < 0)
			throw new IllegalArgumentException(n);
	}
}
