package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public final class SecurityWarrantDelegationTrust {
	private SecurityWarrantDelegationTrust() {
	}

	public static void verifyIssuer(SecurityWarrant parent, SecurityWarrant child,
			ISecurityWarrantDelegationTrustResolver trust, Instant now, String tenant) {
		if (parent == null || child == null || trust == null)
			throw new NullPointerException();
		var issuer = trust.resolve(child.issuer());
		if (issuer == null)
			throw new IllegalStateException("Delegation issuer '" + child.issuer() + "' is not trusted.");
		if (!issuer.canDelegate())
			throw new IllegalStateException("Issuer is trusted for verification but is not authorized to delegate.");
		if (!issuer.allowsKey(child.keyId()))
			throw new IllegalStateException("Delegation was not signed by a trusted issuer key.");
		if (issuer.getKeyState(child.keyId()) != DelegationIssuerKeyState.ACTIVE)
			throw new IllegalStateException("Only an active issuer key may authorize a new delegation.");
		if (!issuer.allowsAudience(child.audience()))
			throw new IllegalStateException("Delegation audience is outside issuer trust scope.");
		if (!issuer.allowsTenant(tenant))
			throw new IllegalStateException("Delegation tenant is outside issuer trust scope.");
		if (!java.util.Objects.equals(child.issuer(), parent.subject()))
			throw new IllegalStateException("Delegation issuer must be the parent subject.");
		if (!parent.isTimeValid(now))
			throw new IllegalStateException("Parent authority is no longer valid.");
		if (child.expiresAt().isAfter(parent.expiresAt()))
			throw new IllegalStateException("Delegation cannot extend parent validity.");
		if (child.issuedAt().isBefore(parent.issuedAt()))
			throw new IllegalStateException("Delegation cannot predate parent authority.");
		if (!java.util.Objects.equals(child.parentDigest(), parent.digest()))
			throw new IllegalStateException("Delegation is not bound to the exact parent warrant.");
	}
}
