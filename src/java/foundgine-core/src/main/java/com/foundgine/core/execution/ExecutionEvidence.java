package com.foundgine.core.execution;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Execution.ExecutionEvidence}.
 *
 * <p>Provider-neutral provenance for one execution. Evidence describes what
 * was executed and which authorization boundaries were present without
 * retaining request objects, expression trees, or provider-specific runtime
 * state.
 */
public record ExecutionEvidence(
        String provider,
        String planFingerprint,
        List<Integer> authorizedNodeIds,
        int rowsReturned,
        long elapsedMilliseconds,
        String providerOperationFingerprint,
        String intentFingerprint,
        String authorizationFingerprint,
        String warrantId,
        String warrantDigest,
        String securityInvariantDigest,
        Long authorizationVersion) {

    public ExecutionEvidence(
            String provider,
            String planFingerprint,
            List<Integer> authorizedNodeIds,
            int rowsReturned,
            long elapsedMilliseconds) {
        this(provider, planFingerprint, authorizedNodeIds, rowsReturned, elapsedMilliseconds,
                null, null, null, null, null, null, null);
    }
}
