package com.foundgine.core.semantic;

/**
 * Stable semantic version identifiers carried across discovery, planning,
 * approval and execution.
 */
public record SemanticVersionSet(String semanticModelVersion, int capabilityContractVersion, int capabilityVersion,
		int intentVersion, int planVersion) {
	public static final int CURRENT_CAPABILITY_CONTRACT_VERSION = 1, CURRENT_CAPABILITY_VERSION = 1,
			CURRENT_INTENT_VERSION = 1, CURRENT_PLAN_VERSION = 1;

	public static SemanticVersionSet forModel(SemanticModel model) {
		if (model == null)
			throw new NullPointerException("model");
		return new SemanticVersionSet("sha256:" + model.contractFingerprint(), CURRENT_CAPABILITY_CONTRACT_VERSION,
				CURRENT_CAPABILITY_VERSION, CURRENT_INTENT_VERSION, CURRENT_PLAN_VERSION);
	}
}