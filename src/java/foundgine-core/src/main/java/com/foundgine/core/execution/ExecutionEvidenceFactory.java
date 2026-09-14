package com.foundgine.core.execution;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.List;
import java.util.stream.StreamSupport;

/** Port of {@code Foundgine.Core.Execution.ExecutionEvidenceFactory}. */
public final class ExecutionEvidenceFactory {

	private ExecutionEvidenceFactory() {
	}

	public static ExecutionEvidence create(String provider, String planFingerprint, Iterable<Integer> authorizedNodeIds,
			int rowsReturned, long elapsedMilliseconds, String providerOperation) {
		if (provider == null || provider.isBlank()) {
			throw new IllegalArgumentException("provider must not be null or blank");
		}
		if (planFingerprint == null || planFingerprint.isBlank()) {
			throw new IllegalArgumentException("planFingerprint must not be null or blank");
		}

		List<Integer> sortedNodeIds = StreamSupport.stream(authorizedNodeIds.spliterator(), false).sorted().toList();

		return new ExecutionEvidence(provider, planFingerprint, sortedNodeIds, rowsReturned, elapsedMilliseconds,
				providerOperation == null ? null : hash(providerOperation), null, null, null, null, null, null);
	}

	/**
	 * Port of {@code ExecutionEvidenceFactory.Hash}: lowercase-hex SHA-256,
	 * matching {@code Convert.ToHexString}'s uppercase form lowercased for Java's
	 * {@link HexFormat} default.
	 */
	public static String hash(String value) {
		try {
			MessageDigest digest = MessageDigest.getInstance("SHA-256");
			byte[] bytes = digest.digest(value.getBytes(StandardCharsets.UTF_8));
			return HexFormat.of().withUpperCase().formatHex(bytes);
		} catch (NoSuchAlgorithmException e) {
			throw new IllegalStateException("SHA-256 is not available", e);
		}
	}
}
