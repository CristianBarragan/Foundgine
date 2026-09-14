package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.AuthorizationId;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.ConnectionId;

public record AuthorizationMetadata(AuthorizationId id, ConnectionId connectionId, String name, String sourceMember,
		Class<?> contextType, Class<?> resourceType, String expression, AuthorizationPredicate predicate) {
	public AuthorizationMetadata(AuthorizationId id, ConnectionId connectionId, String name, String sourceMember,
			Class<?> contextType, Class<?> resourceType, String expression) {
		this(id, connectionId, name, sourceMember, contextType, resourceType, expression, null);
	}
}
