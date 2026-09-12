package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Security.Warrants.SecurityWarrantDelegationTrustTransition}.
 *
 * <p>
 * Pairs delegation-time trust validation ({@link #validateAndCapture}) with an
 * execution-time re-check ({@link #assertUnchanged}) so a delegation is
 * authorized against a trust snapshot, and that exact snapshot — not a fresh
 * lookup — is what execution later confirms is still current.
 */
public final class SecurityWarrantDelegationTrustTransition {
	private SecurityWarrantDelegationTrustTransition() {
	}

	/** Captures the exact issuer trust state used to authorize a delegation. */
	public static DelegationIssuerTrustSnapshot validateAndCapture(SecurityWarrant parent, SecurityWarrant child,
			ISecurityWarrantDelegationTrustStateResolver trust, Instant now, String tenant) {
		if (parent == null || child == null || trust == null) {
			throw new NullPointerException();
		}

		SecurityWarrantDelegationTrust.verifyIssuer(parent, child, trust, now, tenant);
		var snapshot = trust.capture(child.issuer(), child.keyId());
		if (snapshot.keyState() != DelegationIssuerKeyState.ACTIVE) {
			throw new IllegalStateException("Only an active issuer key may authorize a new delegation.");
		}
		return snapshot;
	}

	public static DelegationIssuerTrustSnapshot validateAndCapture(SecurityWarrant parent, SecurityWarrant child,
			ISecurityWarrantDelegationTrustStateResolver trust, Instant now) {
		return validateAndCapture(parent, child, trust, now, null);
	}

	/**
	 * Final execution gate: trust and key lifecycle must be unchanged since
	 * validation.
	 */
	public static void assertUnchanged(DelegationIssuerTrustSnapshot snapshot,
			ISecurityWarrantDelegationTrustStateResolver trust) {
		if (snapshot == null) {
			throw new NullPointerException();
		}
		snapshot.assertCurrent(trust);
	}
}
