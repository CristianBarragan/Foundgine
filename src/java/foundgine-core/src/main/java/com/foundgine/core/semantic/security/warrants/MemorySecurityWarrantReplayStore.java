package com.foundgine.core.semantic.security.warrants;

import java.util.concurrent.ConcurrentHashMap;

public final class MemorySecurityWarrantReplayStore implements ISecurityWarrantReplayStore {
	private final ConcurrentHashMap<String, Boolean> used = new ConcurrentHashMap<>();

	public boolean tryConsume(String warrantId, String nonce) {
		return used.putIfAbsent(warrantId + "\u001f" + nonce, Boolean.TRUE) == null;
	}
}