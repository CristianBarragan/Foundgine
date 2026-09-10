package com.foundgine.core.execution.mutation;

import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.MutationExecutionSecurityGate}.
 *
 * <p>Certifies that a provider can execute a given {@link ExecutionMutationIR}
 * while preserving every security invariant it requires, and re-checks that
 * certificate at the exact point of execution.
 */
public final class MutationExecutionSecurityGate {

    private static final Set<String> PROVIDER_OWNED_INVARIANTS = Set.of(
            SecurityInvariantIds.PARAMETERIZED_VALUES,
            SecurityInvariantIds.ATOMIC_MUTATION,
            SecurityInvariantIds.MUTATION_ROW_LOCKING);

    private MutationExecutionSecurityGate() {
    }

    public static MutationExecutionSecurityCertificate certify(
            ExecutionMutationIR ir,
            Object provider,
            String providerName,
            Iterable<String> upstreamPreserved) {
        Objects.requireNonNull(ir);
        Objects.requireNonNull(provider);
        if (providerName == null || providerName.isBlank())
            throw new IllegalArgumentException("providerName must not be null or blank.");
        Objects.requireNonNull(upstreamPreserved);

        List<String> required = ir.requiredSecurityInvariants();
        if (required.isEmpty())
            throw new IllegalStateException(
                    "Mutation execution requires an explicit non-empty security invariant contract.");

        for (String id : required)
            if (!SecurityInvariantRegistry.contains(id))
                throw new IllegalStateException("Unknown required security invariant '" + id + "'.");

        Set<String> preserved = new LinkedHashSet<>();
        upstreamPreserved.forEach(preserved::add);

        List<String> providerRequired = required.stream().filter(PROVIDER_OWNED_INVARIANTS::contains).toList();

        if (!providerRequired.isEmpty() && !(provider instanceof IMutationSecurityConformanceEvaluator)) {
            throw new IllegalStateException(
                    "Mutation provider '" + provider.getClass().getSimpleName()
                            + "' has no concrete security conformance evaluator for: "
                            + String.join(", ", providerRequired) + ".");
        }

        if (provider instanceof IMutationSecurityConformanceEvaluator concrete) {
            MutationSecurityConformanceResult result = concrete.evaluate(ir);
            if (!Objects.equals(result.provider(), providerName))
                throw new IllegalStateException(
                        "Mutation provider conformance identity '" + result.provider() + "' does not match '" + providerName + "'.");

            result.ensureSatisfied(providerRequired);
            preserved.addAll(result.satisfied());
        }

        MutationExecutionSecurityCertificate certificate =
                MutationExecutionSecurityCertificate.create(ir, provider, providerName, required, preserved);
        certificate.ensureSatisfied();
        return certificate;
    }

    public static void ensureExecutable(
            ExecutionMutationIR ir,
            Object provider,
            MutationExecutionSecurityCertificate certificate) {
        Objects.requireNonNull(ir);
        Objects.requireNonNull(provider);
        Objects.requireNonNull(certificate);

        if (!certificate.isBoundTo(ir, provider))
            throw new IllegalStateException(
                    "Mutation execution certificate is not bound to the exact mutation IR and provider instance being executed.");

        certificate.ensureSatisfied();
    }
}
