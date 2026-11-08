package com.foundgine.providers.gateway;
import java.util.List;
public record FoundgineGatewayProviderDescriptor(String id, String name, String version, List<String> capabilities) {
    public FoundgineGatewayProviderDescriptor {
        require(id, "id"); require(name, "name"); require(version, "version");
        capabilities = List.copyOf(capabilities == null ? List.of() : capabilities);
    }
    public static FoundgineGatewayProviderDescriptor from(IFoundgineProvider provider) {
        if (provider == null) throw new IllegalArgumentException("provider is required");
        var names = java.util.Arrays.stream(FoundgineProviderCapability.values())
                .filter(c -> (provider.capabilities() & c.bit()) == c.bit()).map(Enum::name).toList();
        return new FoundgineGatewayProviderDescriptor(provider.id(), provider.name(), provider.version(), names);
    }
    private static void require(String value, String field) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException(field + " is required");
    }
}
