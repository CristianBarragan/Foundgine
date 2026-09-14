package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import java.util.*;

public record SemanticAuthorizationCapability(EntityId entityId, String name, AuthorizationDecision read,
		AuthorizationDecision write, List<SemanticFieldAuthorizationCapability> fields,
		List<SemanticRelationshipAuthorizationCapability> relationships) {
	public SemanticAuthorizationCapability {
		fields = List.copyOf(fields);
		relationships = List.copyOf(relationships);
	}
}
