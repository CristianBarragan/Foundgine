package com.foundgine.core.execution;

import com.fasterxml.jackson.databind.JsonNode;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Instant;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/** Port of {@code Foundgine.Core.Execution.ExecutionReceiptFactory}. */
public final class ExecutionReceiptFactory {

	private ExecutionReceiptFactory() {
	}

	public static ExecutionReceipt create(String requestId, ExecutionEvidence evidence, String resultFingerprint,
			Iterable<Integer> affectedNodeIds, Iterable<String> effects, Instant startedAt, Instant completedAt,
			int capabilityContractVersion, int capabilityVersion, int intentVersion, int planVersion,
			String semanticModelVersion, String approvalId, String approvedBy, Instant approvedAt) {
		if (requestId == null || requestId.isBlank()) {
			throw new IllegalArgumentException("requestId must not be null or blank");
		}
		if (evidence == null) {
			throw new NullPointerException("evidence");
		}
		if (resultFingerprint == null || resultFingerprint.isBlank()) {
			throw new IllegalArgumentException("resultFingerprint must not be null or blank");
		}
		if (evidence.intentFingerprint() == null) {
			throw new IllegalStateException("Execution evidence is missing an intent fingerprint.");
		}
		if (evidence.authorizationFingerprint() == null) {
			throw new IllegalStateException("Execution evidence is missing an authorization fingerprint.");
		}

		List<Integer> distinctSortedNodeIds = distinctSorted(affectedNodeIds, Integer::compareTo);
		List<String> distinctSortedEffects = distinctSorted(effects, String::compareTo);

		return new ExecutionReceipt(requestId, "succeeded", semanticModelVersion, capabilityContractVersion,
				capabilityVersion, intentVersion, planVersion, evidence.intentFingerprint(), evidence.planFingerprint(),
				evidence.authorizationFingerprint(), evidence.provider(), startedAt, completedAt, distinctSortedNodeIds,
				distinctSortedEffects, resultFingerprint, approvalId, approvedBy, approvedAt, evidence.warrantId(),
				evidence.warrantDigest(), evidence.securityInvariantDigest());
	}

	public static ExecutionReceipt create(String requestId, ExecutionEvidence evidence, String resultFingerprint,
			Iterable<Integer> affectedNodeIds, Iterable<String> effects, Instant startedAt, Instant completedAt,
			int capabilityContractVersion, int capabilityVersion, int intentVersion, int planVersion,
			String semanticModelVersion) {
		return create(requestId, evidence, resultFingerprint, affectedNodeIds, effects, startedAt, completedAt,
				capabilityContractVersion, capabilityVersion, intentVersion, planVersion, semanticModelVersion, null,
				null, null);
	}

	public static String fingerprintResult(ExecutionResult result) {
		if (result == null) {
			throw new NullPointerException("result");
		}

		StringBuilder canonical = new StringBuilder(512);
		for (ExecutionRow row : result.rows()) {
			Map<String, Object> ordered = new TreeMap<>(String::compareTo);
			ordered.putAll(row.values());
			for (Map.Entry<String, Object> pair : ordered.entrySet()) {
				canonical.append(pair.getKey()).append('=');
				appendValue(canonical, pair.getValue());
				canonical.append('|');
			}
			canonical.append("row;");
		}

		if (result.pageInfo() != null) {
			ExecutionPageInfo page = result.pageInfo();
			canonical.append("page[").append(page.startCursor()).append('|').append(page.endCursor()).append('|')
					.append(page.hasNextPage()).append('|').append(page.hasPreviousPage()).append(']');
		}

		return hexSha256(canonical.toString());
	}

	private static void appendValue(StringBuilder builder, Object value) {
		if (value == null) {
			builder.append("null");
			return;
		}

		if (value instanceof JsonNode json) {
			builder.append(json.toString());
		} else if (value instanceof byte[] bytes) {
			builder.append(HexFormat.of().withUpperCase().formatHex(bytes));
		} else if (value instanceof String text) {
			builder.append(text.length()).append(':').append(text);
		} else {
			// Java's Number/Instant/etc. toString() implementations are already
			// culture-invariant, unlike C#'s, so no separate IFormattable branch
			// (InvariantCulture formatting) is needed here.
			builder.append(value);
		}
	}

	private static <T> List<T> distinctSorted(Iterable<T> values, java.util.Comparator<T> comparator) {
		java.util.LinkedHashSet<T> distinct = new java.util.LinkedHashSet<>();
		values.forEach(distinct::add);
		List<T> sorted = new java.util.ArrayList<>(distinct);
		sorted.sort(comparator);
		return sorted;
	}

	private static String hexSha256(String value) {
		try {
			MessageDigest digest = MessageDigest.getInstance("SHA-256");
			byte[] bytes = digest.digest(value.getBytes(StandardCharsets.UTF_8));
			return HexFormat.of().withUpperCase().formatHex(bytes);
		} catch (NoSuchAlgorithmException e) {
			throw new IllegalStateException("SHA-256 is not available", e);
		}
	}
}
