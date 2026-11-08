package com.foundgine.providers.gateway;
import java.util.List;
import java.util.Objects;
/** Stable, transport-neutral description of the gateway surface. */
public record FoundgineGatewayDescription(String name, String contractVersion, List<FoundgineGatewayCapability> capabilities) {
    public FoundgineGatewayDescription {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("name is required");
        if (contractVersion == null || contractVersion.isBlank()) throw new IllegalArgumentException("contractVersion is required");
        capabilities = List.copyOf(Objects.requireNonNull(capabilities, "capabilities"));
    }
}
