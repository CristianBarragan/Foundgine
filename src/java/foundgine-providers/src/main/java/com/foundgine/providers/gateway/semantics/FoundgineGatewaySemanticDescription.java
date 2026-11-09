package com.foundgine.providers.gateway.semantics;

import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;

/** Stable response containing the native semantic contract and compatibility versions. */
public record FoundgineGatewaySemanticDescription(SemanticCapabilityContract contract, SemanticVersionSet versions) { }
