package com.foundgine.core.execution.security;

import com.foundgine.core.execution.SecurityInvariantAttestation;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;

import java.util.*;

/** Provider-neutral declaration of security guarantees. */
public record ProviderSecurityConformanceProfile(
        String provider,
        Collection<String> preservedSecurityInvariants,
        Collection<String> notes) {
    public ProviderSecurityConformanceProfile {
        Objects.requireNonNull(provider, "provider");
        preservedSecurityInvariants = List.copyOf(preservedSecurityInvariants == null ? List.of() : preservedSecurityInvariants);
        notes = List.copyOf(notes == null ? List.of() : notes);
    }

    public SecurityInvariantAttestation evaluate(Collection<String> requiredInvariants) {
        Objects.requireNonNull(requiredInvariants, "requiredInvariants");
        for (var invariant : requiredInvariants) {
            if (!SecurityInvariantRegistry.contains(invariant))
                throw new IllegalStateException("Unknown required security invariant '" + invariant + "'.");
        }
        return SecurityInvariantAttestation.create(provider, requiredInvariants, preservedSecurityInvariants);
    }
}

