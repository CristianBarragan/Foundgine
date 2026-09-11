package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public final class SecurityWarrantAttenuator {
    public static final int MAX_DELEGATION_DEPTH = 32;
    private SecurityWarrantAttenuator() {}
    public static SecurityWarrant attenuate(SecurityWarrant parent, SecurityWarrant child, Instant now) {
        if (parent == null || child == null) throw new NullPointerException();
        if (!parent.isTimeValid(now)) throw new IllegalStateException("Cannot attenuate an expired or not-yet-valid parent warrant.");
        if (!java.util.Objects.equals(child.parentId(), parent.id())) throw new IllegalStateException("Child warrant must identify its parent warrant.");
        if (!java.util.Objects.equals(child.parentDigest(), parent.digest())) throw new IllegalStateException("Child warrant must bind cryptographically to its parent digest.");
        if (!java.util.Objects.equals(child.issuer(), parent.subject())) throw new IllegalStateException("Delegated issuer must be the parent subject.");
        if (!java.util.Objects.equals(child.audience(), parent.audience())) throw new IllegalStateException("Delegation cannot broaden the audience.");
        if (child.subject() == null || child.subject().isEmpty()) throw new IllegalStateException("Delegated subject is required.");
        if (child.issuedAt().isBefore(parent.issuedAt())) throw new IllegalStateException("Child warrant cannot predate parent issuance.");
        if (child.expiresAt().isAfter(parent.expiresAt())) throw new IllegalStateException("Child warrant cannot extend parent expiry.");
        if (child.delegationDepth() != parent.delegationDepth() + 1) throw new IllegalStateException("Delegation depth must increase exactly one level.");
        if (child.delegationDepth() > MAX_DELEGATION_DEPTH) throw new IllegalStateException("Maximum delegation depth exceeded.");
        if (child.delegationPath().size() < parent.delegationPath().size() || !child.delegationPath().subList(0, parent.delegationDepth()).equals(parent.delegationPath()) || !java.util.Objects.equals(child.delegationPath().get(child.delegationDepth()-1), parent.digest()))
            throw new IllegalStateException("Child delegation path does not match the parent chain.");
        if (new java.util.HashSet<>(child.delegationPath()).size() != child.delegationPath().size()) throw new IllegalStateException("Delegation cycle detected.");
        if (!child.constraints().isAtMostAsPowerfulAs(parent.constraints())) throw new IllegalStateException("Child warrant constraints broaden parent authority.");
        for (CapabilityGrant grant : child.grants()) {
            CapabilityGrant pg = parent.grants().stream().filter(x -> x.capability().equals(grant.capability()) && x.operation().equals(grant.operation())).findFirst().orElse(null);
            if (pg == null) throw new IllegalStateException("Child warrant adds capability '" + grant.capability() + "'.");
            if (!pg.resourceScopes().isEmpty() && grant.resourceScopes().stream().anyMatch(x -> !pg.resourceScopes().contains(x)))
                throw new IllegalStateException("Child warrant broadens resource scope for '" + grant.capability() + "'.");
        }
        return child;
    }
}
