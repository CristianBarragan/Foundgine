package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public final class SecurityWarrantDelegationCompromiseGuard {
	private SecurityWarrantDelegationCompromiseGuard() {
	}

	public static void validate(SecurityWarrant w, ISecurityWarrantDelegationCompromiseStore s, Instant n) {
		if (w == null || s == null)
			throw new NullPointerException();
		if (!w.isTimeValid(n))
			throw new IllegalStateException("Security warrant is expired or not yet valid.");
		if (s.isCompromised(w) || s.isCompromisedByAncestor(w) || s.isCompromisedByIssuerOrKey(w))
			throw new IllegalStateException("Security warrant belongs to a compromised delegation subtree.");
	}

	public static SecurityWarrantRevocationSnapshot capture(ISecurityWarrantDelegationCompromiseStore s) {
		if (s == null)
			throw new NullPointerException();
		return new SecurityWarrantRevocationSnapshot(s.currentSequence());
	}

	public static void assertUnchanged(ISecurityWarrantDelegationCompromiseStore s,
			SecurityWarrantRevocationSnapshot x) {
		if (s.currentSequence() != x.sequence())
			throw new IllegalStateException("Delegation compromise state changed during execution.");
	}
}
