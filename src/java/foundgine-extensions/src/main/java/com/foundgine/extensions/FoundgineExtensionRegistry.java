package com.foundgine.extensions;

import java.util.*;

/** Immutable, deterministic registry for optional Foundgine extensions. */
public final class FoundgineExtensionRegistry {
    private final Map<String, FoundgineExtension> extensions;

    private FoundgineExtensionRegistry(Map<String, FoundgineExtension> extensions) {
        this.extensions = Collections.unmodifiableMap(new LinkedHashMap<>(extensions));
    }

    public static Builder builder() { return new Builder(); }

    public Optional<FoundgineExtension> find(String id) {
        if (id == null || id.isBlank()) return Optional.empty();
        return Optional.ofNullable(extensions.get(id));
    }

    public Collection<FoundgineExtension> all() {
        return extensions.values();
    }

    public static final class Builder {
        private final Map<String, FoundgineExtension> extensions = new TreeMap<>();

        public Builder add(FoundgineExtension extension) {
            Objects.requireNonNull(extension, "extension");
            var id = Objects.requireNonNull(extension.id(), "extension.id");
            if (id.isBlank()) throw new IllegalArgumentException("extension.id cannot be blank");
            if (extensions.putIfAbsent(id, extension) != null) {
                throw new IllegalArgumentException("Duplicate extension id: " + id);
            }
            return this;
        }

        public FoundgineExtensionRegistry build() {
            return new FoundgineExtensionRegistry(extensions);
        }
    }
}
