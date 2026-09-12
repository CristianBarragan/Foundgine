package com.foundgine.core.semantic.planning;

/** Provider-neutral estimate of work introduced by a rewrite. */
public record RewriteCost(double estimatedWork) {
	public static RewriteCost from(double value) {
		validate(value);
		return new RewriteCost(value);
	}

	private static void validate(double value) {
		if (Double.isNaN(value) || Double.isInfinite(value) || value < 0)
			throw new IllegalArgumentException("Rewrite cost must be finite and non-negative.");
	}
}
