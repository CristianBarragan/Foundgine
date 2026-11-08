package com.foundgine.providers.gateway;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
/** Pure capability negotiation. Capabilities from different providers are never merged. */
public final class FoundgineProviderNegotiator {
    private FoundgineProviderNegotiator() { }
    public static FoundgineProviderNegotiationResult negotiate(List<? extends IFoundgineProvider> providers,
            List<FoundgineProviderRequirement> requirements) {
        if (providers == null || requirements == null) throw new IllegalArgumentException("arguments are required");
        Set<String> ids = new HashSet<>();
        for (var provider : providers) {
            if (provider == null || provider.id() == null || provider.id().isBlank()) throw new IllegalArgumentException("provider identity is required");
            if (!ids.add(provider.id().toLowerCase(java.util.Locale.ROOT))) throw new IllegalArgumentException("Duplicate provider id '" + provider.id() + "'");
        }
        List<FoundgineProviderNegotiationFailure> failures = new ArrayList<>();
        for (var requirement : requirements) {
            IFoundgineProvider closest = null; long closestMissing = requirement.required(); boolean satisfied = false;
            for (var provider : providers) {
                if (provider.kind() != requirement.kind()) continue;
                long missing = FoundgineProviderCapability.missing(provider.capabilities(), requirement.required());
                if (missing == 0) { satisfied = true; break; }
                if (Long.bitCount(missing) < Long.bitCount(closestMissing)) { closest = provider; closestMissing = missing; }
            }
            if (!satisfied) failures.add(new FoundgineProviderNegotiationFailure(requirement.kind(), closestMissing,
                    closest == null ? null : closest.id(), requirement.reason()));
        }
        return new FoundgineProviderNegotiationResult(failures);
    }
}
