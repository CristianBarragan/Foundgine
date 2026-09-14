package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public final class SecurityWarrantRevocationGuard {
	private SecurityWarrantRevocationGuard() {
	}

	public static SecurityWarrantRevocationSnapshot validate(SecurityWarrant w, ISecurityWarrantRevocationStore s,
			Instant now) {
		if (w == null || s == null)
			throw new NullPointerException();
		if (!w.isTimeValid(now))
			throw new IllegalStateException("Security warrant is expired or not yet valid.");
		if (s.isRevoked(w.id(), w.digest()) || s.isDigestRevoked(w.digest()))
			throw new IllegalStateException("Security warrant has been revoked.");
		for (String d : w.delegationPath())
			if (s.isDigestRevoked(d))
				throw new IllegalStateException("A parent security warrant has been revoked.");
		return SecurityWarrantRevocationSnapshot.capture(s);
	}
}
