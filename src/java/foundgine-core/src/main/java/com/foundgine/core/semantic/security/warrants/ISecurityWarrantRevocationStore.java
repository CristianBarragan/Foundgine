package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public interface ISecurityWarrantRevocationStore {
	long currentSequence();

	SecurityWarrantRevocation revoke(SecurityWarrant warrant, Instant now);

	boolean isRevoked(String id, String digest);

	boolean isDigestRevoked(String digest);
}
