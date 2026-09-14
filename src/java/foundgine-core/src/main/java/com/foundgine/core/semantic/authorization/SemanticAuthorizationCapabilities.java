package com.foundgine.core.semantic.authorization;

import java.util.*;

public record SemanticAuthorizationCapabilities(List<SemanticAuthorizationCapability> entities) {
	public SemanticAuthorizationCapabilities {
		entities = List.copyOf(entities);
	}
}
