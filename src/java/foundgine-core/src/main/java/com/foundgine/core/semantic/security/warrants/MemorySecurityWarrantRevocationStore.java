package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;

public final class MemorySecurityWarrantRevocationStore implements ISecurityWarrantRevocationStore {
	private final Map<String, SecurityWarrantRevocation> revoked = new ConcurrentHashMap<>();
	private final AtomicLong sequence = new AtomicLong();

	public long currentSequence() {
		return sequence.get();
	}

	public SecurityWarrantRevocation revoke(SecurityWarrant w, Instant now) {
		if (w == null)
			throw new NullPointerException();
		if (w.id().isBlank() || w.digest().isBlank())
			throw new IllegalStateException("A warrant must have an identity and digest before revocation.");
		String k = w.id() + "\u001f" + w.digest();
		var e = new SecurityWarrantRevocation(w.id(), w.digest(), now, sequence.incrementAndGet());
		revoked.putIfAbsent(k, e);
		return revoked.get(k);
	}

	public boolean isRevoked(String id, String digest) {
		return revoked.containsKey(id + "\u001f" + digest);
	}

	public boolean isDigestRevoked(String digest) {
		return revoked.values().stream().anyMatch(x -> java.util.Objects.equals(x.warrantDigest(), digest));
	}
}
