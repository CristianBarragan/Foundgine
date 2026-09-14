package com.foundgine.core.semantic.security;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityCapability}.
 *
 * <p>
 * What may be done. Kept distinct from restrictions and execution invariants.
 */
public record SecurityCapability(String id, String operation, String resourceScope) {
}
