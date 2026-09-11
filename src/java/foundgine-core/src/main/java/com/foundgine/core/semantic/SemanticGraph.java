package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import java.util.*;

/** Canonical request graph containing semantic/domain topology only. */
public final class SemanticGraph {
    private final List<SemanticGraphNode> nodes = new ArrayList<>();
    private SemanticQueryOptions options;

    public SemanticGraph() {}

    public SemanticGraph(Collection<SemanticGraphNode> nodes, SemanticQueryOptions options) {
        if (nodes != null) this.nodes.addAll(nodes);
        this.options = options;
    }

    public List<SemanticGraphNode> nodes() { return Collections.unmodifiableList(nodes); }
    public SemanticQueryOptions options() { return options; }
    public void setOptions(SemanticQueryOptions options) { this.options = options; }

    public SemanticGraph withAuthorization(int nodeId, AuthorizationPredicate authorization) {
        Objects.requireNonNull(authorization);
        if (nodes.stream().noneMatch(n -> n.id() == nodeId)) throw new IllegalArgumentException("Unknown node: " + nodeId);
        return new SemanticGraph(nodes.stream().map(n -> n.id() == nodeId ? n.withAuthorization(authorization) : n).toList(), options);
    }

    public SemanticGraphNode addRoot(EntityId entityId, Collection<FieldId> fields, AuthorizationPredicate authorization) {
        return add(entityId, null, null, null, fields, authorization);
    }
    public SemanticGraphNode addRoot(EntityId entityId) { return addRoot(entityId, List.of(), null); }
    public SemanticGraphNode addRoot(EntityId entityId, Collection<FieldId> fields) { return addRoot(entityId, fields, null); }

    public SemanticGraphNode addConnection(EntityId entityId, ConnectionId connectionId, SemanticGraphNode parent,
                                           Collection<FieldId> fields, AuthorizationPredicate authorization) {
        return add(entityId, null, connectionId, parent, fields, authorization);
    }
    public SemanticGraphNode addConnection(EntityId entityId, ConnectionId connectionId, SemanticGraphNode parent) {
        return addConnection(entityId, connectionId, parent, List.of(), null);
    }

    public SemanticGraphNode add(EntityId entityId, RelationshipId relationshipId, SemanticGraphNode parent,
                                 Collection<FieldId> fields) {
        return add(entityId, relationshipId, null, parent, fields, null);
    }
    public SemanticGraphNode add(EntityId entityId, RelationshipId relationshipId, SemanticGraphNode parent,
                                 Collection<FieldId> fields, AuthorizationPredicate authorization) {
        return add(entityId, relationshipId, null, parent, fields, authorization);
    }

    private SemanticGraphNode add(EntityId entityId, RelationshipId relationshipId, ConnectionId connectionId,
                                  SemanticGraphNode parent, Collection<FieldId> fields, AuthorizationPredicate authorization) {
        Objects.requireNonNull(entityId);
        if (relationshipId != null && connectionId != null) throw new IllegalArgumentException("A semantic node cannot be reached through both a relationship and a connection.");
        if (parent == null && connectionId != null) throw new IllegalArgumentException("A root semantic node cannot be reached through a connection.");
        var node = new SemanticGraphNode(nodes.size(), entityId, relationshipId, connectionId,
                parent == null ? null : parent.id(), authorization,
                fields == null ? List.of() : fields.stream().distinct().toList());
        nodes.add(node);
        return node;
    }

    public record SemanticGraphNode(
        int id, EntityId entityId, RelationshipId viaRelationship, ConnectionId viaConnection, Integer parentId,
        AuthorizationPredicate authorization, List<FieldId> fields, Map<String,String> semanticAnnotations,
        SemanticTraversalOrigin traversalOrigin, SemanticIntentOrigin intentOrigin,
        SemanticExpectedCardinality expectedCardinality, boolean nullablePath, List<SemanticConstraint> semanticConstraints) {
        public SemanticGraphNode {
            Objects.requireNonNull(entityId);
            fields = fields == null ? List.of() : List.copyOf(fields);
            semanticAnnotations = semanticAnnotations == null ? Map.of() : Collections.unmodifiableMap(new LinkedHashMap<>(semanticAnnotations));
            expectedCardinality = expectedCardinality == null ? SemanticExpectedCardinality.UNKNOWN : expectedCardinality;
            semanticConstraints = semanticConstraints == null ? List.of() : List.copyOf(semanticConstraints);
        }
        public SemanticGraphNode(int id, EntityId entityId, RelationshipId viaRelationship, ConnectionId viaConnection, Integer parentId, AuthorizationPredicate authorization) {
            this(id, entityId, viaRelationship, viaConnection, parentId, authorization, List.of(), Map.of(), null, null, SemanticExpectedCardinality.UNKNOWN, false, List.of());
        }
        public SemanticGraphNode(int id, EntityId entityId, RelationshipId viaRelationship, ConnectionId viaConnection, Integer parentId, AuthorizationPredicate authorization, List<FieldId> fields) {
            this(id, entityId, viaRelationship, viaConnection, parentId, authorization, fields, Map.of(), null, null, SemanticExpectedCardinality.UNKNOWN, false, List.of());
        }
        public SemanticGraphNode withAuthorization(AuthorizationPredicate p) {
            return new SemanticGraphNode(id, entityId, viaRelationship, viaConnection, parentId, p, fields, semanticAnnotations, traversalOrigin, intentOrigin, expectedCardinality, nullablePath, semanticConstraints);
        }
    }

    public record SemanticTraversalOrigin(EntityId sourceEntity, List<RelationshipId> path) {
        public SemanticTraversalOrigin { path = path == null ? List.of() : List.copyOf(path); }
    }
    public record SemanticIntentOrigin(String operation, String source) {}
    public enum SemanticExpectedCardinality { UNKNOWN, ONE, MANY }
}
