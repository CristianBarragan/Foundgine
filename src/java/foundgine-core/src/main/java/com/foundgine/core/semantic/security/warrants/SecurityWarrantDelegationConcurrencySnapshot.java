package com.foundgine.core.semantic.security.warrants;

public record SecurityWarrantDelegationConcurrencySnapshot(String parentWarrantId, String parentWarrantDigest,
		long sequence) {
}