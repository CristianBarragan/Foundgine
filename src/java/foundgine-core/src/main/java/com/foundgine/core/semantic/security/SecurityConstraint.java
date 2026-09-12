package com.foundgine.core.semantic.security;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityConstraint}.
 *
 * <p>
 * Under what restrictions a capability may be exercised.
 */
public record SecurityConstraint(String name, String value) {
}
