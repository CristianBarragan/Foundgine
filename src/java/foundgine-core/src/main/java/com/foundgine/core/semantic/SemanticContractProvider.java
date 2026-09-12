package com.foundgine.core.semantic;

import java.util.Objects;

/** Default provider for a frozen semantic contract snapshot. */
public final class SemanticContractProvider implements ISemanticContractProvider {
	private final SemanticContractSnapshot contract;

	public SemanticContractProvider(SemanticContractSnapshot contract) {
		this.contract = Objects.requireNonNull(contract);
	}

	@Override
	public SemanticContractSnapshot contract() {
		return contract;
	}
}
