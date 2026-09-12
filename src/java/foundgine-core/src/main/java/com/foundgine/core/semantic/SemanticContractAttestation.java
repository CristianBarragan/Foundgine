package com.foundgine.core.semantic;

import java.util.Locale;
import java.util.Objects;

/** Verifies that a semantic model matches an expected contract fingerprint. */
public final class SemanticContractAttestation {
	private SemanticContractAttestation() {
	}

	public static boolean matches(SemanticModel model, String expectedFingerprint) {
		Objects.requireNonNull(model);
		requireText(expectedFingerprint);
		return model.contractFingerprint().equals(normalize(expectedFingerprint));
	}

	public static void ensureMatches(SemanticModel model, String expectedFingerprint) {
		Objects.requireNonNull(model);
		requireText(expectedFingerprint);
		var expected = normalize(expectedFingerprint);
		if (model.contractFingerprint().equals(expected))
			return;
		throw new IllegalStateException("Semantic contract attestation failed. Expected fingerprint '" + expected
				+ "', but the runtime semantic model has fingerprint '" + model.contractFingerprint() + "'.");
	}

	private static void requireText(String s) {
		if (s == null || s.isBlank())
			throw new IllegalArgumentException("Fingerprint cannot be empty.");
	}

	private static String normalize(String s) {
		var x = s.trim();
		return x.regionMatches(true, 0, "sha256:", 0, 7) ? x.substring(7).toLowerCase(Locale.ROOT)
				: x.toLowerCase(Locale.ROOT);
	}
}
