package com.foundgine.core.execution;

/**
 * Current authorization authority state observed immediately before execution.
 */
public record ExecutionAuthorizationAuthorityState(long version, String fingerprint, boolean allowed) {
	public ExecutionAuthorizationAuthorityState(long version, String fingerprint) {
		this(version, fingerprint, true);
	}
}
