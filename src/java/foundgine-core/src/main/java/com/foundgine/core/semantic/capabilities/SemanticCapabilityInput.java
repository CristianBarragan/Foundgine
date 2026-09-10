package com.foundgine.core.semantic.capabilities;

/**
 * Port of {@code Foundgine.Core.Semantic.Capabilities.SemanticCapabilityInput}.
 *
 * <p>Describes one input accepted by a semantic capability.
 */
public record SemanticCapabilityInput(String name, String type, boolean required, String description) {

    /** Convenience overload for the C# {@code Description = null} default. */
    public SemanticCapabilityInput(String name, String type, boolean required) {
        this(name, type, required, null);
    }
}
