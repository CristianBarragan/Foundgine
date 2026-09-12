package com.foundgine.core.semantic.security.warrants;

public interface ISecurityWarrantReplayStore {
	boolean tryConsume(String warrantId, String nonce);
}
