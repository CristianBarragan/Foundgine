package com.foundgine.core.semantic.metadata;
/** Static declaration of a historical or synonymous semantic name. */
public record AliasDeclaration(String name, Integer weight) {
    public AliasDeclaration {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Alias name is required.");
        if (weight != null && (weight < 1 || weight > 100))
            throw new IllegalArgumentException("Alias weight must be between 1 and 100 (inclusive) when specified.");
    }
    public AliasDeclaration(String name) { this(name, null); }
    @Override public String toString() { return name; }
}
