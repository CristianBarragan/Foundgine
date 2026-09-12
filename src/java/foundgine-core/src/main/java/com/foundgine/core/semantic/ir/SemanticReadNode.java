package com.foundgine.core.semantic.ir;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;

public record SemanticReadNode(int id, EntityId entityId, List<FieldId> fields, RelationshipId viaRelationship,
		ConnectionId viaConnection, List<SemanticReadNode> children, SemanticQueryOptions queryOptions,
		AuthorizationPredicate authorization, List<FieldId> requiredFields) {
	public SemanticReadNode {
		Objects.requireNonNull(entityId);
		fields = fields == null ? List.of() : List.copyOf(fields);
		children = children == null ? List.of() : List.copyOf(children);
		requiredFields = requiredFields == null ? List.of() : List.copyOf(requiredFields);
	}

	public SemanticReadNode(int id, EntityId entityId, List<FieldId> fields, RelationshipId viaRelationship,
			ConnectionId viaConnection, List<SemanticReadNode> children, SemanticQueryOptions queryOptions,
			AuthorizationPredicate authorization) {
		this(id, entityId, fields, viaRelationship, viaConnection, children, queryOptions, authorization, List.of());
	}

	public boolean isRoot() {
		return viaRelationship == null && viaConnection == null;
	}

	public SemanticReadNode withFields(List<FieldId> value) {
		return new SemanticReadNode(id, entityId, value, viaRelationship, viaConnection, children, queryOptions,
				authorization, requiredFields);
	}

	public SemanticReadNode withChildren(List<SemanticReadNode> value) {
		return new SemanticReadNode(id, entityId, fields, viaRelationship, viaConnection, value, queryOptions,
				authorization, requiredFields);
	}

	public SemanticReadNode withAuthorization(AuthorizationPredicate value) {
		return new SemanticReadNode(id, entityId, fields, viaRelationship, viaConnection, children, queryOptions, value,
				requiredFields);
	}
}
