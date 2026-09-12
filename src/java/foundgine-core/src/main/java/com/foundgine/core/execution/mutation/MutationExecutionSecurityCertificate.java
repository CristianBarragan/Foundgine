package com.foundgine.core.execution.mutation;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Objects;

/**
 *
 * <p>In-process execution certificate bound to one exact mutation IR and one
 * exact provider instance. It is deliberately non-serializable/non-transferable.
 *
 * <p>The factory method and bind check are package-private
 * ({@link #create} and {@link #isBoundTo}) rather than public, since this
 * certificate is only meant to be constructed and checked from within this
 * package.
 */
public final class MutationExecutionSecurityCertificate {

    private final ExecutionMutationIR boundIr;
    private final Object boundProvider;

    private final String provider;
    private final String irFingerprint;
    private final List<String> required;
    private final List<String> preserved;
    private final List<String> missing;

    private MutationExecutionSecurityCertificate(
            ExecutionMutationIR boundIr,
            Object boundProvider,
            String provider,
            List<String> required,
            List<String> preserved,
            List<String> missing) {
        this.boundIr = boundIr;
        this.boundProvider = boundProvider;
        this.provider = provider;
        this.required = required;
        this.preserved = preserved;
        this.missing = missing;
        this.irFingerprint = MutationExecutionIRFingerprint.create(boundIr);
    }

    public String provider() {
        return provider;
    }

    public String irFingerprint() {
        return irFingerprint;
    }

    public List<String> required() {
        return required;
    }

    public List<String> preserved() {
        return preserved;
    }

    public List<String> missing() {
        return missing;
    }

    public boolean isSatisfied() {
        return missing.isEmpty();
    }

    static MutationExecutionSecurityCertificate create(
            ExecutionMutationIR ir,
            Object provider,
            String providerName,
            Iterable<String> required,
            Iterable<String> preserved) {
        Objects.requireNonNull(ir);
        Objects.requireNonNull(provider);
        if (providerName == null || providerName.isBlank())
            throw new IllegalArgumentException("providerName must not be null or blank.");
        Objects.requireNonNull(required);
        Objects.requireNonNull(preserved);

        List<String> requiredSet = distinctSorted(required);
        List<String> preservedSet = distinctSorted(preserved);
        LinkedHashSet<String> preservedLookup = new LinkedHashSet<>(preservedSet);
        List<String> missing = requiredSet.stream().filter(id -> !preservedLookup.contains(id)).sorted().toList();

        return new MutationExecutionSecurityCertificate(
                ir, provider, providerName, requiredSet, preservedSet, missing);
    }

    boolean isBoundTo(ExecutionMutationIR ir, Object provider) {
        return boundIr == ir
                && boundProvider == provider
                && Objects.equals(irFingerprint, MutationExecutionIRFingerprint.create(ir));
    }

    public void ensureSatisfied() {
        if (!isSatisfied()) {
            throw new IllegalStateException(
                    "Mutation provider '" + provider + "' cannot satisfy required security invariants: "
                            + String.join(", ", missing) + ".");
        }
    }

    private static List<String> distinctSorted(Iterable<String> values) {
        LinkedHashSet<String> distinct = new LinkedHashSet<>();
        for (String value : values)
            distinct.add(value);
        return distinct.stream().sorted().toList();
    }
}