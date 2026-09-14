package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;

public final class MemorySecurityWarrantDelegationCompromiseStore implements ISecurityWarrantDelegationCompromiseStore {
	private final Map<String, SecurityWarrantDelegationCompromise> c = new ConcurrentHashMap<>();
	private final AtomicLong seq = new AtomicLong();

	public long currentSequence() {
		return seq.get();
	}

	public SecurityWarrantDelegationCompromise compromise(SecurityWarrant r, Instant n, String i, String k) {
		if (r == null)
			throw new NullPointerException();
		if ((i == null || i.isBlank()) && (k == null || k.isBlank()))
			throw new IllegalArgumentException("A compromised issuer or key must be supplied.");
		var e = new SecurityWarrantDelegationCompromise(r.id(), r.digest(), i, k, n, seq.incrementAndGet());
		c.putIfAbsent(r.id() + "\u001f" + r.digest(), e);
		return c.get(r.id() + "\u001f" + r.digest());
	}

	public boolean isCompromised(SecurityWarrant w) {
		return c.containsKey(w.id() + "\u001f" + w.digest());
	}

	public boolean isCompromisedByAncestor(SecurityWarrant w) {
		return w.delegationPath().stream()
				.anyMatch(d -> c.values().stream().anyMatch(x -> Objects.equals(x.rootWarrantDigest(), d)));
	}

	public boolean isCompromisedByIssuerOrKey(SecurityWarrant w) {
		for (var x : c.values()) {
			if (!Objects.equals(x.rootWarrantDigest(), w.digest())
					&& !w.delegationPath().contains(x.rootWarrantDigest()))
				continue;
			if (x.compromisedIssuer() != null && Objects.equals(x.compromisedIssuer(), w.issuer()))
				return true;
			if (x.compromisedKeyId() != null && Objects.equals(x.compromisedKeyId(), w.keyId()))
				return true;
		}
		return false;
	}
}
