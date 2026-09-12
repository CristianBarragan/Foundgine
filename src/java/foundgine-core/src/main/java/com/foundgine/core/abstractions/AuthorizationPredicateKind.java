package com.foundgine.core.abstractions;

/** Port of {@code Foundgine.Core.Abstractions.AuthorizationPredicateKind}. */
public enum AuthorizationPredicateKind {
	PARAMETER, CONTEXT_PARAMETER, RESOURCE_PARAMETER, MEMBER_ACCESS, CONSTANT, EQUAL, NOT_EQUAL, AND, OR, NOT
}
