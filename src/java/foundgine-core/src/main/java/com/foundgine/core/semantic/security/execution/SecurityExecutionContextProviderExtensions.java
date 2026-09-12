package com.foundgine.core.semantic.security.execution;

import java.util.Objects;

/** Shared fail-closed helper for transport adapters. */
public final class SecurityExecutionContextProviderExtensions {
	private SecurityExecutionContextProviderExtensions() {
	}

	/**
	 * Returns the current trusted context or throws when the host has not
	 * established one. This intentionally fails closed.
	 */
	public static SecurityExecutionContext requireSecurityExecutionContext(ISecurityExecutionContextProvider provider,
			String transportName, String operationDescription) {
		Objects.requireNonNull(provider, "provider");
		if (transportName == null || transportName.isBlank())
			throw new IllegalArgumentException("Value cannot be null or whitespace: transportName");
		if (operationDescription == null || operationDescription.isBlank())
			throw new IllegalArgumentException("Value cannot be null or whitespace: operationDescription");

		SecurityExecutionContext context = provider.getSecurityExecutionContext();
		if (context == null) {
			throw new SecurityException(transportName + " " + operationDescription
					+ " requires a host-supplied SecurityExecutionContext. " + "The " + transportName
					+ " caller cannot supply identity, tenant, audience, or warrant context.");
		}
		return context;
	}
}
