package com.foundgine.runtime;

import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimitValidator;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;

import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.MutationSecurityResourceLimitValidator}.
 *
 * <p>
 * Canonical mutation-side resource guard. It runs before mutation planning,
 * authorization, provider lowering, or replay consumption.
 */
public final class MutationSecurityResourceLimitValidator {

	private MutationSecurityResourceLimitValidator() {
	}

	public static void validate(SemanticMutationRequest request, SecurityResourceLimits limits) {
		Objects.requireNonNull(request, "request");
		Objects.requireNonNull(limits, "limits");
		limits.validate();
		Objects.requireNonNull(request.graph(), "request.graph");

		List<SemanticMutationOperation> operations = request.graph().operations();
		if (operations.isEmpty())
			reject("A semantic mutation graph must contain at least one operation.");
		if (operations.size() > limits.maxMutationOperations())
			reject("Mutation operation count exceeds the configured maximum of " + limits.maxMutationOperations()
					+ ".");

		int dependencyCount = operations.stream().mapToInt(x -> x.dependencies().size()).sum();
		if (dependencyCount > limits.maxMutationDependencies())
			reject("Mutation dependency count exceeds the configured maximum of " + limits.maxMutationDependencies()
					+ ".");

		int effectCount = operations.stream().mapToInt(x -> x.effects().size()).sum();
		if (effectCount > limits.maxMutationEffects())
			reject("Mutation effect count exceeds the configured maximum of " + limits.maxMutationEffects() + ".");

		for (int i = 0; i < operations.size(); i++) {
			SemanticMutationOperation operation = operations.get(i);
			if (operation.fields().size() > limits.maxMutationFieldsPerOperation())
				reject("Mutation field count for operation " + i + " exceeds the configured maximum of "
						+ limits.maxMutationFieldsPerOperation() + ".");
			if (operation.returnFields().size() > limits.maxMutationReturnFieldsPerOperation())
				reject("Mutation return-field count for operation " + i + " exceeds the configured maximum of "
						+ limits.maxMutationReturnFieldsPerOperation() + ".");

			if (operation.filter() != null)
				SecurityResourceLimitValidator.validateFilter(operation.filter(), limits);
		}
	}

	private static void reject(String message) {
		throw new IllegalStateException(message);
	}
}
