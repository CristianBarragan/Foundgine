package com.foundgine.runtime;

import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.mutation.SemanticMutationIntentBuilder;

import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.FoundgineMutationExtensions}.
 *
 * <p>
 * Convenience entry point for advanced/open mutation authoring.
 *
 * <p>
 * C# exposes this as an extension method ({@code mutations.Mutate(model)});
 * Java has no extension methods, so the port is a static factory taking the
 * receiver as an explicit first parameter, matching this port's established
 * convention for ported C# extension methods.
 */
public final class FoundgineMutations {

	private FoundgineMutations() {
	}

	/**
	 * Creates an open mutation builder over the application's semantic model. The
	 * resulting graph is executed by the supplied {@link IFoundgineMutations}
	 * instance, so authorization, invariants, approval and provider execution
	 * remain centralized.
	 */
	public static SemanticMutationIntentBuilder mutate(IFoundgineMutations mutations, SemanticModel model) {
		Objects.requireNonNull(mutations, "mutations");
		return new SemanticMutationIntentBuilder(model);
	}
}
