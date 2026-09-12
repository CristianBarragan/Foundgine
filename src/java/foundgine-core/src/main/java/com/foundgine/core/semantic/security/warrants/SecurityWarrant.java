package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;
import java.util.*;

public record SecurityWarrant(String id, String issuer, String subject, String audience, List<CapabilityGrant> grants,
		SecurityWarrantConstraints constraints, Instant issuedAt, Instant expiresAt, String nonce, String keyId,
		String parentId, byte[] signature, String parentDigest, List<String> delegationPath) {
	public SecurityWarrant {
		grants = List.copyOf(grants == null ? List.of() : grants);
		constraints = Objects.requireNonNull(constraints, "constraints");
		delegationPath = List.copyOf(delegationPath == null ? List.of() : delegationPath);
		signature = signature == null ? new byte[0] : signature.clone();
	}

	public static SecurityWarrant ofDefaults(String id, String issuer, String subject, String audience,
			List<CapabilityGrant> grants, SecurityWarrantConstraints constraints, Instant issuedAt, Instant expiresAt,
			String nonce, String keyId, String parentId, byte[] signature) {
		return new SecurityWarrant(id, issuer, subject, audience, grants, constraints, issuedAt, expiresAt, nonce,
				keyId, parentId, signature, null, List.of());
	}

	public int delegationDepth() {
		return delegationPath.size();
	}

	public String digest() {
		return SecurityWarrantCanonicalizer.digest(this);
	}

	public boolean isTimeValid(Instant now) {
		return !now.isBefore(issuedAt) && now.isBefore(expiresAt);
	}

	public byte[] signature() {
		return signature.clone();
	}

	public SecurityWarrant withSignature(byte[] s) {
		return new SecurityWarrant(id, issuer, subject, audience, grants, constraints, issuedAt, expiresAt, nonce,
				keyId, parentId, s, parentDigest, delegationPath);
	}
}
