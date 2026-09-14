package com.foundgine.core.semantic.security;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityInvariant}.
 *
 * <p>
 * Stable machine-readable security invariant attached to a semantic capability
 * or execution plan. Invariants describe required guarantees; they do not grant
 * authorization and never replace execution-time policy evaluation.
 */
public record SecurityInvariant(String id, String name, String description, SecurityInvariantPhase phase,
		boolean mustBePreservedByProvider) {
}
