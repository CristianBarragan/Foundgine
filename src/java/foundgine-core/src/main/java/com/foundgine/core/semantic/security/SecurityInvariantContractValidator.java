package com.foundgine.core.semantic.security;

import com.foundgine.core.semantic.capabilities.SemanticCapability;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Security.SecurityInvariantContractValidator}.
 *
 * <p>
 * Validates the machine-readable security contract before a capability is
 * allowed to cross into provider planning. This is a structural proof gate, not
 * an authorization decision.
 */
public final class SecurityInvariantContractValidator {

	private SecurityInvariantContractValidator() {
	}

	public static List<String> validate(SemanticCapability capability) {
		Objects.requireNonNull(capability, "capability");

		List<String> errors = new ArrayList<>();
		List<String> invariants = capability.effectiveSecurityInvariants();

		// Validate explicitly supplied identifiers independently of the derived
		// default set. This keeps unknown identifiers observable and fail-closed
		// even when the capability also carries field/relationship metadata.
		for (String id : capability.requiredSecurityInvariants()) {
			if (!SecurityInvariantRegistry.contains(id)) {
				errors.add("Capability '" + capability.id() + "' references unknown security invariant '" + id + "'.");
			}
		}

		if (capability.hasSideEffects() && !invariants.contains(SecurityInvariantIds.RUNTIME_AUTHORIZATION)) {
			errors.add("Mutating capability '" + capability.id() + "' must require security invariant '"
					+ SecurityInvariantIds.RUNTIME_AUTHORIZATION + "'.");
		}

		if (capability.hasSideEffects() && !invariants.contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED)) {
			errors.add("Mutating capability '" + capability.id() + "' must require security invariant '"
					+ SecurityInvariantIds.AUTHORIZATION_REQUIRED + "'.");
		}

		if (!capability.fields().isEmpty() && !invariants.contains(SecurityInvariantIds.FIELD_VISIBILITY)) {
			errors.add("Capability '" + capability.id() + "' exposes fields without security invariant '"
					+ SecurityInvariantIds.FIELD_VISIBILITY + "'.");
		}

		if (!capability.relationships().isEmpty()
				&& !invariants.contains(SecurityInvariantIds.RELATIONSHIP_VISIBILITY)) {
			errors.add("Capability '" + capability.id()
					+ "' exposes relationships without a relationship-visibility invariant.");
		}

		return errors;
	}

	public static void ensureValid(SemanticCapability capability) {
		List<String> errors = validate(capability);
		if (!errors.isEmpty()) {
			throw new IllegalStateException(String.join(" ", errors));
		}
	}

	public static void ensureContractValid(SemanticCapabilityContract contract) {
		Objects.requireNonNull(contract, "contract");
		List<String> errors = new ArrayList<>();
		for (SemanticCapability capability : contract.capabilities()) {
			errors.addAll(validate(capability));
		}
		if (!errors.isEmpty()) {
			throw new IllegalStateException(String.join(System.lineSeparator(), errors));
		}
	}
}
