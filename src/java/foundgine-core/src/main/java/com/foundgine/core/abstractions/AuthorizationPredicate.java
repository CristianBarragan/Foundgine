package com.foundgine.core.abstractions;

/**
 * Port of {@code Foundgine.Core.Abstractions.AuthorizationPredicate}.
 *
 * <p>
 * Small provider-independent representation of an AOT authorization predicate.
 * It contains no expression trees and no executable delegates.
 *
 * <p>
 * {@code name}, {@code value}, {@code left}, and {@code right} are nullable;
 * which ones are populated depends on {@code kind}, mirroring the C#
 * optional-parameter constructor.
 */
public record AuthorizationPredicate(AuthorizationPredicateKind kind, String name, String value,
		AuthorizationPredicate left, AuthorizationPredicate right) {

	public static AuthorizationPredicate parameter(String name) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.PARAMETER, name, null, null, null);
	}

	public static AuthorizationPredicate contextParameter(String name) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.CONTEXT_PARAMETER, name, null, null, null);
	}

	public static AuthorizationPredicate resourceParameter(String name) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.RESOURCE_PARAMETER, name, null, null, null);
	}

	public static AuthorizationPredicate member(AuthorizationPredicate target, String name) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.MEMBER_ACCESS, name, null, target, null);
	}

	public static AuthorizationPredicate constant(String value) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.CONSTANT, null, value, null, null);
	}

	public static AuthorizationPredicate equal(AuthorizationPredicate left, AuthorizationPredicate right) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.EQUAL, null, null, left, right);
	}

	public static AuthorizationPredicate notEqual(AuthorizationPredicate left, AuthorizationPredicate right) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.NOT_EQUAL, null, null, left, right);
	}

	public static AuthorizationPredicate and(AuthorizationPredicate left, AuthorizationPredicate right) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.AND, null, null, left, right);
	}

	public static AuthorizationPredicate or(AuthorizationPredicate left, AuthorizationPredicate right) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.OR, null, null, left, right);
	}

	public static AuthorizationPredicate not(AuthorizationPredicate operand) {
		return new AuthorizationPredicate(AuthorizationPredicateKind.NOT, null, null, operand, null);
	}
}
