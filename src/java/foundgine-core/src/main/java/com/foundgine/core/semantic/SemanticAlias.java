package com.foundgine.core.semantic;

import java.util.Objects;

/** Historical semantic name resolving to the owning canonical declaration. */
public record SemanticAlias(String name, Integer weight) {
    public SemanticAlias {
        Objects.requireNonNull(name);
        if (weight != null && (weight < 1 || weight > 100)) throw new IllegalArgumentException("Alias weight must be between 1 and 100 (inclusive) when specified.");
    }
    public SemanticAlias(String name) { this(name, null); }
    @Override public String toString() { return name; }
}
