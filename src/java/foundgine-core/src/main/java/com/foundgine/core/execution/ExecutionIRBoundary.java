package com.foundgine.core.execution;

import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;

import java.util.Objects;

/** Verifies authorization provenance survives the execution boundary. */
public final class ExecutionIRBoundary {
	private ExecutionIRBoundary() {
	}

	public static void ensureAuthorized(SemanticContractSnapshot contract, ExecutionIR ir,
			SemanticAuthorizationEvidence evidence) {
		Objects.requireNonNull(contract, "contract");
		Objects.requireNonNull(ir, "ir");
		Objects.requireNonNull(evidence, "evidence");
		var binding = ir.authorizationBinding();
		if (binding == null)
			throw new IllegalStateException("Execution IR is missing authorization provenance.");
		if (!binding.contractFingerprint().equals(contract.contractFingerprint()))
			throw new IllegalStateException("Execution IR belongs to a different semantic contract.");
		if (!binding.authorizationFingerprint().equals(evidence.authorizationFingerprint()))
			throw new IllegalStateException("Execution IR belongs to a different authorization decision.");
		evidence.ensureMatches(contract);
	}

	public static void bindProviderPlan(ExecutionIR ir, ProviderPlan providerPlan) {
		Objects.requireNonNull(ir, "ir");
		Objects.requireNonNull(providerPlan, "providerPlan");
		var binding = ir.authorizationBinding();
		if (binding == null)
			throw new IllegalStateException("Execution IR is missing authorization provenance.");
		providerPlan.bindAuthorization(binding);
	}

	public static void ensureProviderPlan(SemanticContractSnapshot contract, ExecutionIR ir, ProviderPlan providerPlan,
			SemanticAuthorizationEvidence evidence) {
		ensureAuthorized(contract, ir, evidence);
		Objects.requireNonNull(providerPlan, "providerPlan");
		if (providerPlan.authorizationBinding() == null)
			throw new IllegalStateException("Provider plan is missing authorization provenance.");
		if (providerPlan.authorizationBinding() != ir.authorizationBinding())
			throw new IllegalStateException("Provider plan authorization provenance does not match the execution IR.");
		providerPlan.authorizationBinding().ensureMatches(contract, evidence);
	}
}
