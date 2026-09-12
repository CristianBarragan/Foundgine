package com.foundgine.runtime;

import java.util.Objects;

/**
 * Context supplied to an enabled Foundgine capability during startup
 * configuration.
 */
public final class FoundgineCapabilityContext {
	private final FoundgineOptions options;
	private final FoundgineServiceRegistry services;

	public FoundgineCapabilityContext(FoundgineOptions options, FoundgineServiceRegistry services) {
		this.options = Objects.requireNonNull(options, "options");
		this.services = Objects.requireNonNull(services, "services");
	}

	public FoundgineOptions options() {
		return options;
	}

	public FoundgineServiceRegistry services() {
		return services;
	}
}
