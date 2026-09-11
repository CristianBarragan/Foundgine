package com.foundgine.core.semantic.security;

import com.foundgine.core.semantic.capabilities.SemanticCapability;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityCapabilityCompositionResult}.
 */
public record SecurityCapabilityCompositionResult(
        boolean isSatisfied,
        List<SemanticCapability> components,
        List<String> effectiveSecurityInvariants,
        String failureReason) {

    public SecurityCapabilityCompositionResult {
        components = components == null ? List.of() : List.copyOf(components);
        effectiveSecurityInvariants = effectiveSecurityInvariants == null ? List.of() : List.copyOf(effectiveSecurityInvariants);
    }

    public static SecurityCapabilityCompositionResult accepted(List<SemanticCapability> components, List<String> invariants) {
        return new SecurityCapabilityCompositionResult(true, components, invariants, null);
    }

    public static SecurityCapabilityCompositionResult rejected(String reason) {
        return new SecurityCapabilityCompositionResult(false, List.of(), List.of(), reason);
    }
}
