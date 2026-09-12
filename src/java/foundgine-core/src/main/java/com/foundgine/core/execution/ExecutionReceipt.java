package com.foundgine.core.execution;

import java.time.Duration;
import java.time.Instant;
import java.util.List;

/**
 * Port of {@code Foundgine.Core.Execution.ExecutionReceipt}.
 *
 * <p>
 * Immutable, provider-neutral evidence that a semantic execution occurred. The
 * receipt deliberately contains fingerprints rather than request/result
 * payloads so it can be persisted or transported without copying domain data.
 */
public record ExecutionReceipt(String requestId, String status, String semanticModelVersion,
		int capabilityContractVersion, int capabilityVersion, int intentVersion, int planVersion,
		String intentFingerprint, String planFingerprint, String authorizationFingerprint, String provider,
		Instant startedAt, Instant completedAt, List<Integer> affectedNodeIds, List<String> effects,
		String resultFingerprint, String approvalId, String approvedBy, Instant approvedAt, String warrantId,
		String warrantDigest, String securityInvariantDigest) {

	public long elapsedMilliseconds() {
		long millis = Duration.between(startedAt, completedAt).toMillis();
		return Math.max(0, millis);
	}
}
