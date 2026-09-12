package com.foundgine.core.semantic.security.warrants;

public final class SecurityWarrantDelegationConcurrencyGuard {
	private SecurityWarrantDelegationConcurrencyGuard() {
	}

	public static SecurityWarrantDelegationConcurrencySnapshot capture(ISecurityWarrantDelegationConcurrencyStore s,
			SecurityWarrant p) {
		if (s == null || p == null)
			throw new NullPointerException();
		return s.capture(p);
	}

	public static SecurityWarrantDelegationReservation commitChild(ISecurityWarrantDelegationConcurrencyStore s,
			SecurityWarrant p, SecurityWarrant c, SecurityWarrantDelegationConcurrencySnapshot x) {
		if (s == null)
			throw new NullPointerException();
		return s.commitChild(p, c, x);
	}
}