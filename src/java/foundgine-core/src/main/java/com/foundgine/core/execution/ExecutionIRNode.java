package com.foundgine.core.execution;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.ConnectionId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.planning.AggregateExecutionStrategy;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import com.foundgine.core.semantic.query.SemanticQueryOptions;

import java.util.List;
import java.util.Objects;

/** Provider-neutral executable node. */

public record ExecutionIRNode(int id, ExecutionOperation operation, EntityId entityId, List<FieldId> fields,
		RelationshipId viaRelationship, ConnectionId viaConnection, List<ExecutionIRNode> children,
		SemanticQueryOptions queryOptions, AuthorizationPredicate authorization,
		AggregateExecutionStrategy aggregateExecutionStrategy) {

	public ExecutionIRNode {
		Objects.requireNonNull(operation, "operation");
		Objects.requireNonNull(entityId, "entityId");
		fields = fields == null ? List.of() : List.copyOf(fields);
		children = children == null ? List.of() : List.copyOf(children);
		aggregateExecutionStrategy = aggregateExecutionStrategy == null ? AggregateExecutionStrategy.DEFAULT
				: aggregateExecutionStrategy;
	}

	static ExecutionIRNode from(SemanticPlanNode node) {
		return new ExecutionIRNode(node.id(), node.operation(), node.entityId(), node.fields(), node.viaRelationship(),
				node.viaConnection(), node.children().stream().map(ExecutionIRNode::from).toList(), node.queryOptions(),
				node.authorization(), node.aggregateExecutionStrategy());
	}
}
