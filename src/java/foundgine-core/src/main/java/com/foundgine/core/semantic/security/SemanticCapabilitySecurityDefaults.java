package com.foundgine.core.semantic.security;

import com.foundgine.core.abstractions.AuthorizationAccess;
import com.foundgine.core.semantic.capabilities.SemanticCapability;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Security.SemanticCapabilitySecurityDefaults}.
 *
 * <p>
 * Derives the minimum security contract for generic semantic capabilities.
 *
 * <p>
 * Named {@code forCapability} rather than the C# name {@code For} because
 * {@code for} is a reserved keyword in Java.
 */
public final class SemanticCapabilitySecurityDefaults {

	private SemanticCapabilitySecurityDefaults() {
	}

	public static List<String> forCapability(SemanticCapability capability) {
		Set<String> ids = new LinkedHashSet<>();
		ids.add(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		ids.add(SecurityInvariantIds.PARAMETERIZED_VALUES);

		if (!capability.fields().isEmpty()) {
			ids.add(SecurityInvariantIds.FIELD_VISIBILITY);
		}
		if (!capability.relationships().isEmpty()) {
			ids.add(SecurityInvariantIds.RELATIONSHIP_VISIBILITY);
		}
		if (capability.hasSideEffects() || capability.access().access() == AuthorizationAccess.CONDITIONAL) {
			ids.add(SecurityInvariantIds.RUNTIME_AUTHORIZATION);
		}

		List<String> sorted = new ArrayList<>(ids);
		sorted.sort(String::compareTo);
		return sorted;
	}
}
