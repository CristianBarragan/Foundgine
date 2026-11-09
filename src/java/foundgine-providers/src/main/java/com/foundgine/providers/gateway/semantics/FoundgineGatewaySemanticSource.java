package com.foundgine.providers.gateway.semantics;

import com.foundgine.runtime.IFoundgine;

/** Adapts the existing {@link IFoundgine} application surface for gateway consumers. */
public final class FoundgineGatewaySemanticSource implements IFoundgineGatewaySemanticSource {
    private final IFoundgine foundgine;

    public FoundgineGatewaySemanticSource(IFoundgine foundgine) {
        if (foundgine == null) throw new IllegalArgumentException("foundgine is required");
        this.foundgine = foundgine;
    }

    /**
     * The host must authenticate and authorize access to this discovery endpoint. The returned contract is descriptive
     * only; every execution still goes through {@code IFoundgine} and its execution-time authorization checks.
     */
    @Override public FoundgineGatewaySemanticDescription describe() {
        return new FoundgineGatewaySemanticDescription(foundgine.describeCapabilityContract(), foundgine.describeVersionSet());
    }
}
