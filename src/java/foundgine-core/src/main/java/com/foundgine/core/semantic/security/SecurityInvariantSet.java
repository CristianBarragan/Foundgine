package com.foundgine.core.semantic.security;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Semantic.Security.SecurityInvariantSet}.
 *
 * <p>Immutable, deterministically ordered invariant requirements.
 */
public record SecurityInvariantSet(List<SecurityInvariant> invariants) {

    public boolean contains(String id) {
        return invariants.stream().anyMatch(x -> x.id().equals(id));
    }

    public void require(String id) {
        if (!contains(id)) {
            throw new IllegalStateException("Required security invariant '" + id + "' is missing.");
        }
    }
}
