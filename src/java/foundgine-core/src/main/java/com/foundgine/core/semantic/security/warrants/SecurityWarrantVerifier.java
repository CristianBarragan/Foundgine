package com.foundgine.core.semantic.security.warrants;

import java.security.*;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;

public final class SecurityWarrantVerifier {
	private SecurityWarrantVerifier() {
	}

	public static void verify(SecurityWarrant w, ISecurityWarrantKeyResolver keys, Instant now, String expectedIssuer,
			String expectedAudience) {
		if (w == null || keys == null)
			throw new NullPointerException();
		if (w.signature().length == 0)
			throw new IllegalStateException("Security warrant has no signature.");
		if (!w.expiresAt().isAfter(w.issuedAt()))
			throw new IllegalStateException("Security warrant expiry must be after issued-at.");
		if (!w.isTimeValid(now))
			throw new IllegalStateException("Security warrant is expired or not yet valid.");
		if (expectedIssuer == null)
			throw new IllegalStateException("Security warrant verification requires a configured expected issuer.");
		if (!w.issuer().equals(expectedIssuer))
			throw new IllegalStateException("Security warrant issuer is not trusted.");
		if (expectedAudience != null && !w.audience().equals(expectedAudience))
			throw new IllegalStateException("Security warrant audience is not trusted.");
		RSAPublicKey key = keys.resolve(w.keyId());
		if (key == null)
			throw new IllegalStateException("Unknown warrant key '" + w.keyId() + "'.");
		try {
			Signature s = Signature.getInstance("SHA256withRSA");
			s.initVerify(key);
			s.update(SecurityWarrantCanonicalizer.unsignedBytes(w.withSignature(new byte[0])));
			if (!s.verify(w.signature()))
				throw new IllegalStateException("Security warrant signature is invalid.");
		} catch (GeneralSecurityException e) {
			throw new IllegalStateException("Unable to verify security warrant signature.", e);
		}
	}

	public static void verify(SecurityWarrant w, ISecurityWarrantKeyResolver k, Instant n, String issuer) {
		verify(w, k, n, issuer, null);
	}
}
