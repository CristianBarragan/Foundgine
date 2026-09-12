package com.foundgine.runtime.api;

import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.mutation.SemanticMutationIntentBuilder;
import java.util.Objects;

public final class FoundgineMutationExtensions {
	private FoundgineMutationExtensions() {
	}

	public static SemanticMutationIntentBuilder mutate(IFoundgineMutations mutations, SemanticModel model) {
		Objects.requireNonNull(mutations, "mutations");
		return new SemanticMutationIntentBuilder(model);
	}
}
