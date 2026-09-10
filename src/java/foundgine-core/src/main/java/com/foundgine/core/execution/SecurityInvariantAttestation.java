package com.foundgine.core.execution;

import java.util.LinkedHashSet;
import java.util.List;

/**
 * Port of {@code Foundgine.Core.Execution.SecurityInvariantAttestation}.
 *
 * <p>Provider conformance evidence. This name deliberately distinguishes a
 * checked contract result from a mathematical proof of implementation
 * safety.
 *
 * <p>The C# file's {@code using Foundgine.Core.Semantic.Security;} is unused
 * by the type itself (it references no {@code Semantic.Security} type), so —
 * unlike most of {@code Foundgine.Core.Execution} — this class has no
 * dependency on the not-yet-ported {@code Semantic} package and could be
 * ported now.
 */
public record SecurityInvariantAttestation(
        String provider,
        List<String> required,
        List<String> preserved,
        List<String> missing) {

    public boolean isSatisfied() {
        return missing.isEmpty();
    }

    public void ensureSatisfied() {
        if (!isSatisfied()) {
            throw new IllegalStateException(
                    "Provider '" + provider + "' cannot satisfy required security invariants: "
                            + String.join(", ", missing) + ".");
        }
    }

    public static SecurityInvariantAttestation create(
            String provider,
            Iterable<String> required,
            Iterable<String> preserved) {
        List<String> requiredSet = distinctSortedOrdinal(required);
        List<String> preservedSet = distinctSortedOrdinal(preserved);
        List<String> missing = requiredSet.stream()
                .filter(item -> !preservedSet.contains(item))
                .sorted(String::compareTo)
                .toList();
        return new SecurityInvariantAttestation(provider, requiredSet, preservedSet, missing);
    }

    private static List<String> distinctSortedOrdinal(Iterable<String> values) {
        LinkedHashSet<String> distinct = new LinkedHashSet<>();
        values.forEach(distinct::add);
        return distinct.stream().sorted(String::compareTo).toList();
    }
}
