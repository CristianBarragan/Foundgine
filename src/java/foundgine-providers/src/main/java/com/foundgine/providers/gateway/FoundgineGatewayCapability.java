package com.foundgine.providers.gateway;

import java.util.List;
import java.util.Map;
import java.util.Objects;

/** Transport-neutral description of one capability exposed by the gateway. */
public record FoundgineGatewayCapability(String id, String name, String version, String kind,
        String description, List<String> operations, Map<String, String> metadata) {
    public FoundgineGatewayCapability {
        requireText(id, "id"); requireText(name, "name"); requireText(version, "version"); requireText(kind, "kind");
        description = Objects.requireNonNullElse(description, "");
        operations = List.copyOf(Objects.requireNonNull(operations, "operations"));
        metadata = metadata == null ? Map.of() : Map.copyOf(metadata);
    }
    private static void requireText(String value, String field) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException(field + " is required");
    }
}
