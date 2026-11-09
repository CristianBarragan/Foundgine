package com.foundgine.providers.gateway;
public record FoundgineProviderRequirement(FoundgineProviderKind kind, long required, String reason) {
    public FoundgineProviderRequirement { if (kind == null) throw new IllegalArgumentException("kind is required"); }
}
