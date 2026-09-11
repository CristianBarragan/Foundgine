package com.foundgine.core.execution.mutation;

import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.stream.Collectors;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.MutationSecurityConformanceResult}.
 *
 * <p>Concrete provider evidence for mutation execution. Provider declarations are
 * not sufficient to cross the mutation execution boundary.
 */
public record MutationSecurityConformanceResult(
        String provider,
        List<String> satisfied,
        List<String> violations) {

    public MutationSecurityConformanceResult {
        satisfied = satisfied == null ? List.of() : List.copyOf(satisfied);
        violations = violations == null ? List.of() : List.copyOf(violations);
    }

    public void ensureSatisfied(Collection<String> required) {
        LinkedHashSet<String> satisfiedSet = new LinkedHashSet<>(satisfied);
        List<String> missing = required.stream()
                .filter(id -> !satisfiedSet.contains(id))
                .distinct()
                .sorted()
                .toList();

        if (violations.isEmpty() && missing.isEmpty())
            return;

        List<String> reasons = new ArrayList<>(violations);
        reasons.addAll(missing.stream().map(x -> "required invariant '" + x + "' was not satisfied").toList());

        throw new IllegalStateException(
                "Mutation provider '" + provider + "' security conformance failed: "
                        + reasons.stream().collect(Collectors.joining("; ")));
    }
}
