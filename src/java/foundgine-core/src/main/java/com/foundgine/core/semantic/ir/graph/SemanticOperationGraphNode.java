package com.foundgine.core.semantic.ir.graph;
import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;
/** Immutable graph node with explicit semantic edges. */
public record SemanticOperationGraphNode(int id, EntityId entityId, List<FieldId> fields, List<FieldId> requiredFields,
                                         RelationshipId viaRelationship, ConnectionId viaConnection, List<Integer> children,
                                         Integer parentId, SemanticQueryOptions queryOptions, AuthorizationPredicate authorization){
    public SemanticOperationGraphNode{Objects.requireNonNull(entityId);fields=List.copyOf(fields);requiredFields=List.copyOf(requiredFields);children=List.copyOf(children);}
    public boolean isRoot(){return parentId==null;}
}
