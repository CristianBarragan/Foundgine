package com.foundgine.core.semantic.security.warrants;

/** Result of one atomic revocation lookup: the observed sequence and what was found at that sequence. */
public record SecurityWarrantRevocationLookup(long sequence, boolean revoked, boolean ancestorRevoked) {}
