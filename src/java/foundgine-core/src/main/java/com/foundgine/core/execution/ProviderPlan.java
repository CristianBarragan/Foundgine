package com.foundgine.core.execution;

import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import java.util.Objects;

/** Opaque physical plan produced by a provider compiler. */
public abstract class ProviderPlan {
	private final String provider;
	private SecurityInvariantProof securityProof;
	private SemanticPlanAuthorizationBinding authorizationBinding;

	protected ProviderPlan(String provider) {
		if (provider == null || provider.isBlank()) {
			throw new IllegalArgumentException("Provider is required.");
		}
		this.provider = provider;
	}

	public final String provider() {
		return provider;
	}

	public final SecurityInvariantProof securityProof() {
		return securityProof;
	}

	public final SemanticPlanAuthorizationBinding authorizationBinding() {
		return authorizationBinding;
	}

	void setSecurityProof(SecurityInvariantProof proof) {
		this.securityProof = proof;
	}

	void bindAuthorization(SemanticPlanAuthorizationBinding binding) {
		Objects.requireNonNull(binding, "binding");
		if (authorizationBinding != null && authorizationBinding != binding) {
			throw new IllegalStateException(
					"Provider plan authorization provenance cannot be replaced once established.");
		}
		authorizationBinding = binding;
	}
}
