package com.foundgine.core.execution.security;

import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ProviderPlan;

import java.util.List;

/** Concrete conformance evidence produced from the compiled provider plan. */
public record ProviderSecurityConformanceResult(String provider, List<String> required, List<String> satisfied,
		List<String> violations) {

	public boolean isSatisfied() {
		return violations.isEmpty() && required.stream().allMatch(satisfied::contains);
	}

	public void ensureSatisfied() {
		if (isSatisfied())
			return;
		var missing = required.stream().filter(x -> !satisfied.contains(x)).sorted().toList();
		var reasons = new java.util.ArrayList<>(violations);
		missing.forEach(x -> reasons.add("required invariant '" + x + "' was not satisfied"));
		throw new IllegalStateException(
				"Provider '" + provider + "' security conformance failed: " + String.join("; ", reasons));
	}
}
