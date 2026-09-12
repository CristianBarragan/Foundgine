package com.foundgine.core.semantic.capabilities;

import com.foundgine.core.abstractions.AuthorizationDecision;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.security.SemanticCapabilitySecurityDefaults;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Semantic.Capabilities.SemanticCapability}.
 *
 * <p>
 * A named capability exposed by the semantic model.
 *
 * <p>
 * The C# type is a positional record with 9 canonical constructor parameters
 * plus 5 additional {@code init}-only properties that default
 * ({@code Operation = "read"}, {@code HasSideEffects = false},
 * {@code IsIdempotent = false},
 * {@code Version = SemanticVersionSet.CurrentCapabilityVersion},
 * {@code RequiredSecurityInvariants = []}) when not set via object-initializer
 * syntax at the call site. Java records have a single canonical constructor, so
 * all 14 values are record components here; {@link #ofDefaults} is the
 * convenience overload for callers that only need the 9 originally-positional
 * values and want the same defaults C# would apply.
 */
public record SemanticCapability(String id, String name, EntityId targetEntityId, AuthorizationDecision access,
		List<SemanticCapabilityInput> inputs, List<SemanticCapabilityConstraint> constraints,
		List<SemanticCapabilityEffect> effects, List<String> fields, List<String> relationships, String operation,
		boolean hasSideEffects, boolean isIdempotent, int version, List<String> requiredSecurityInvariants) {

	/**
	 * Convenience overload matching the C# call sites that only supply the 9
	 * originally-positional constructor arguments and rely on the C# {@code init}
	 * defaults for the rest ({@code Operation = "read"},
	 * {@code HasSideEffects = false}, {@code IsIdempotent = false},
	 * {@code Version = SemanticVersionSet.CurrentCapabilityVersion},
	 * {@code RequiredSecurityInvariants = []}).
	 */
	public static SemanticCapability ofDefaults(String id, String name, EntityId targetEntityId,
			AuthorizationDecision access, List<SemanticCapabilityInput> inputs,
			List<SemanticCapabilityConstraint> constraints, List<SemanticCapabilityEffect> effects, List<String> fields,
			List<String> relationships) {
		return new SemanticCapability(id, name, targetEntityId, access, inputs, constraints, effects, fields,
				relationships, "read", false, false, SemanticVersionSet.CURRENT_CAPABILITY_VERSION, List.of());
	}

	/**
	 * Port of the C# computed property {@code EffectiveSecurityInvariants}.
	 *
	 * <p>
	 * Returns the canonical invariant set when callers did not explicitly supply
	 * one.
	 */
	public List<String> effectiveSecurityInvariants() {
		return !requiredSecurityInvariants.isEmpty() ? requiredSecurityInvariants
				: SemanticCapabilitySecurityDefaults.forCapability(this);
	}
}
