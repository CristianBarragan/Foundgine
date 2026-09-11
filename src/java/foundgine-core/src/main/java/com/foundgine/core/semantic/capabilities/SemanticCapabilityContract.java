package com.foundgine.core.semantic.capabilities;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContract}.
 *
 * <p>Canonical, machine-readable description of the semantic application
 * surface. The contract is descriptive and never replaces execution-time
 * authorization.
 */
public record SemanticCapabilityContract(int version, List<SemanticCapability> capabilities) {
}
