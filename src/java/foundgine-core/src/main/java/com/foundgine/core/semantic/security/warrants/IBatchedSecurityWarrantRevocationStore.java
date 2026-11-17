package com.foundgine.core.semantic.security.warrants;

import java.util.List;

/**
 * Optional capability: answers the warrant, digest and delegation-ancestor revocation questions together with the
 * sequence they were observed at, as one consistent snapshot. Remote stores can implement this in a single round
 * trip instead of one call per question plus sequence bracketing.
 */
public interface IBatchedSecurityWarrantRevocationStore extends ISecurityWarrantRevocationStore {
    SecurityWarrantRevocationLookup lookup(String warrantId, String warrantDigest, List<String> ancestorDigests);
}
