package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;

/** Provider-neutral canonical semantic planning node. */
public record SemanticPlanNode(int id, ExecutionOperation operation, EntityId entityId,
    List<FieldId> fields, RelationshipId viaRelationship, ConnectionId viaConnection,
    List<SemanticPlanNode> children, SemanticQueryOptions queryOptions,
    AuthorizationPredicate authorization, RelationshipCardinality relationshipCardinality,
    RelationshipTraversalMode traversalMode, int traversalOrder,
    AggregateExecutionStrategy aggregateExecutionStrategy) {
    public SemanticPlanNode {
        Objects.requireNonNull(operation); Objects.requireNonNull(entityId);
        fields=fields==null?List.of():List.copyOf(fields); children=children==null?List.of():List.copyOf(children);
        traversalMode=traversalMode==null?RelationshipTraversalMode.DEFAULT:traversalMode;
        aggregateExecutionStrategy=aggregateExecutionStrategy==null?AggregateExecutionStrategy.DEFAULT:aggregateExecutionStrategy;
    }
    public SemanticPlanNode(int id, ExecutionOperation operation, EntityId entityId,
        List<FieldId> fields, RelationshipId viaRelationship, ConnectionId viaConnection,
        List<SemanticPlanNode> children) { this(id,operation,entityId,fields,viaRelationship,viaConnection,children,null,null,null,RelationshipTraversalMode.DEFAULT,-1,AggregateExecutionStrategy.DEFAULT); }
}
