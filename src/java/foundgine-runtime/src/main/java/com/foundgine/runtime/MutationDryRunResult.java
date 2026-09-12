package com.foundgine.runtime;

import java.util.List;

/**
 * Port of the {@code MutationDryRunResult} record declared alongside
 * {@code IFoundgineMutations}.
 */
public record MutationDryRunResult(String planFingerprint, List<MutationPlanOperation> operations,
		List<String> effects) {

	public MutationDryRunResult {
		operations = operations == null ? List.of() : List.copyOf(operations);
		effects = effects == null ? List.of() : List.copyOf(effects);
	}
}
