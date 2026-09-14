package com.foundgine.core.abstractions;

/**
 * Port of {@code Foundgine.Core.Abstractions.AuthorizationOperationName}.
 *
 * <p>
 * Optional named refinement of an {@link AuthorizationOperation} for policies
 * that distinguish domain-specific write intents (for example "Invoice.Pay"
 * versus "Invoice.Update") beyond the coarse Read/Write gate. This is a
 * policy-facing hint only: it never changes what {@link AuthorizationOperation}
 * means structurally, and it carries no storage or provider semantics.
 */
public record AuthorizationOperationName(String value) {

	@Override
	public String toString() {
		return value;
	}
}
