package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public record SecurityWarrantDelegationCompromise(String rootWarrantId, String rootWarrantDigest,
		String compromisedIssuer, String compromisedKeyId, Instant compromisedAt, long sequence) {
}
