package com.foundgine.core.semantic.authorization;

import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.ir.graph.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.util.*;

/**
 * Immutable authorization evidence bound to the exact semantic contract
 * evaluated.
 */
public record SemanticAuthorizationEvidence(String contractFingerprint, String authorizationFingerprint,
		Long authorizationVersion, String authorizationAuthorityFingerprint) {
	public SemanticAuthorizationEvidence(String c, String a) {
		this(c, a, null, null);
	}

	public void ensureMatches(SemanticContractSnapshot c) {
		Objects.requireNonNull(c);
		if (!contractFingerprint.equals(c.contractFingerprint()))
			throw new SemanticAuthorizationException("Authorization evidence is bound to semantic contract '"
					+ contractFingerprint + "', but the supplied contract is '" + c.contractFingerprint() + "'.");
	}

	public static SemanticAuthorizationEvidence create(SemanticContractSnapshot c, SemanticOperation op) {
		Objects.requireNonNull(c);
		Objects.requireNonNull(op);
		var payload = "contract=" + c.contractFingerprint() + "|operation="
				+ SemanticOperationGraph.create(op).fingerprint();
		return new SemanticAuthorizationEvidence(c.contractFingerprint(), sha256(payload));
	}

	private static String sha256(String s) {
		try {
			var d = MessageDigest.getInstance("SHA-256").digest(s.getBytes(StandardCharsets.UTF_8));
			var b = new StringBuilder();
			for (byte x : d)
				b.append(String.format("%02x", x));
			return b.toString();
		} catch (Exception e) {
			throw new IllegalStateException(e);
		}
	}
}
