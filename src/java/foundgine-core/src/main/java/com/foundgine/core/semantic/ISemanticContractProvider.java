package com.foundgine.core.semantic;

/**
 * Supplies the trusted immutable semantic contract used by runtime components.
 */
public interface ISemanticContractProvider {
	SemanticContractSnapshot contract();
}
