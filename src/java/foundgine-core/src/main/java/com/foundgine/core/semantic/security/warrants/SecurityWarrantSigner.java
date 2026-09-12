package com.foundgine.core.semantic.security.warrants;

import java.security.*;

public final class SecurityWarrantSigner {
	private SecurityWarrantSigner() {
	}

	public static SecurityWarrant sign(SecurityWarrant w, PrivateKey key) {
		if (w == null || key == null)
			throw new NullPointerException();
		try {
			Signature s = Signature.getInstance("SHA256withRSA");
			s.initSign(key);
			s.update(SecurityWarrantCanonicalizer.unsignedBytes(w.withSignature(new byte[0])));
			return w.withSignature(s.sign());
		} catch (GeneralSecurityException e) {
			throw new IllegalStateException("Unable to sign security warrant.", e);
		}
	}
}
