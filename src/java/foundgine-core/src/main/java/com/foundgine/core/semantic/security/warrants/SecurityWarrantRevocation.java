package com.foundgine.core.semantic.security.warrants;
import java.time.Instant;
public record SecurityWarrantRevocation(String warrantId,String warrantDigest,Instant revokedAt,long sequence) {}
