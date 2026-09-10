package com.foundgine.core.execution;

import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;

import java.util.*;

/**
 * Immutable security execution certificate bound to one exact provider plan
 * and one exact Execution IR fingerprint.
 */
public final class SecurityInvariantProof {
    private final ProviderPlan boundPlan;
    private final String executionIrFingerprint;
    private final String provider;
    private final List<String> required;
    private final List<String> preserved;
    private final List<String> missing;

    private SecurityInvariantProof(ProviderPlan boundPlan, String executionIrFingerprint,
                                   String provider, List<String> required,
                                   List<String> preserved, List<String> missing) {
        this.boundPlan = boundPlan;
        this.executionIrFingerprint = executionIrFingerprint;
        this.provider = provider;
        this.required = List.copyOf(required);
        this.preserved = List.copyOf(preserved);
        this.missing = List.copyOf(missing);
    }

    public String provider() { return provider; }
    public List<String> required() { return required; }
    public List<String> preserved() { return preserved; }
    public List<String> missing() { return missing; }
    public String executionIrFingerprint() { return executionIrFingerprint; }
    public boolean isSatisfied() { return missing.isEmpty(); }

    boolean isBoundTo(ProviderPlan plan, ExecutionIR ir) {
        return boundPlan != null && boundPlan == plan
                && executionIrFingerprint.equals(ExecutionIRFingerprint.create(ir));
    }

    public void ensureSatisfied() {
        if (!isSatisfied()) {
            throw new IllegalStateException(
                    "Provider '" + provider + "' cannot satisfy required security invariants: "
                            + String.join(", ", missing) + ".");
        }
    }

    static SecurityInvariantProof create(ProviderPlan plan, ExecutionIR ir,
                                         Collection<String> required, Collection<String> preserved) {
        Objects.requireNonNull(plan, "plan");
        Objects.requireNonNull(ir, "ir");
        List<String> r = sortedDistinct(required);
        List<String> p = sortedDistinct(preserved);
        List<String> m = r.stream().filter(x -> !p.contains(x)).toList();
        return new SecurityInvariantProof(plan, ExecutionIRFingerprint.create(ir), plan.provider(), r, p, m);
    }

    private static List<String> sortedDistinct(Collection<String> values) {
        return values.stream().filter(Objects::nonNull).distinct().sorted().toList();
    }
}
