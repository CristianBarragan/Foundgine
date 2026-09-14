package com.foundgine.core.execution.security;

import com.foundgine.core.execution.SecurityInvariantAttestation;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;

import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.NoSuchElementException;
import java.util.Objects;

/** Explicit provider capability matrix; profiles do not grant authority. */
public final class ProviderSecurityConformanceMatrix {
	private final Map<String, ProviderSecurityConformanceProfile> profiles = new LinkedHashMap<>();

	public synchronized ProviderSecurityConformanceMatrix register(ProviderSecurityConformanceProfile profile) {
		Objects.requireNonNull(profile, "profile");
		if (profile.provider().isBlank())
			throw new IllegalArgumentException("Provider name is required.");
		for (var invariant : profile.preservedSecurityInvariants()) {
			if (!SecurityInvariantRegistry.contains(invariant))
				throw new IllegalStateException("Provider '" + profile.provider()
						+ "' declares unknown security invariant '" + invariant + "'.");
		}
		profiles.put(profile.provider(), profile);
		return this;
	}

	public synchronized List<ProviderSecurityConformanceProfile> profiles() {
		return List.copyOf(profiles.values());
	}

	public synchronized ProviderSecurityConformanceProfile get(String provider) {
		var profile = profiles.get(provider);
		if (profile == null)
			throw new NoSuchElementException(
					"Provider '" + provider + "' is not registered in the security conformance matrix.");
		return profile;
	}

	public SecurityInvariantAttestation evaluate(String provider, Collection<String> required) {
		return get(provider).evaluate(required);
	}

	public SecurityInvariantAttestation ensureSatisfied(String provider, Collection<String> required) {
		var result = evaluate(provider, required);
		result.ensureSatisfied();
		return result;
	}
}
