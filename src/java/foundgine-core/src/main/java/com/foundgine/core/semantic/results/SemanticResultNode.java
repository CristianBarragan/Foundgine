package com.foundgine.core.semantic.results;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import java.util.*;

/** Semantic result node preserving plan topology and selected values. */
public final class SemanticResultNode {
    private final int planNodeId;
    private final EntityId entityId;
    private final Object identityValue;
    private final Map<FieldId, Object> values;
    private final Map<RelationshipId, List<SemanticResultNode>> children = new LinkedHashMap<>();

    public SemanticResultNode(int planNodeId, EntityId entityId, Object identityValue,
                              Map<FieldId, Object> values) {
        this.planNodeId = planNodeId;
        this.entityId = Objects.requireNonNull(entityId, "entityId");
        this.identityValue = Objects.requireNonNull(identityValue, "identityValue");
        this.values = Collections.unmodifiableMap(new LinkedHashMap<>(Objects.requireNonNull(values, "values")));
    }
    public int planNodeId() { return planNodeId; }
    public EntityId entityId() { return entityId; }
    public Object identityValue() { return identityValue; }
    public Map<FieldId, Object> values() { return values; }
    public Map<RelationshipId, List<SemanticResultNode>> children() {
        var copy = new LinkedHashMap<RelationshipId, List<SemanticResultNode>>();
        children.forEach((k,v) -> copy.put(k, List.copyOf(v)));
        return Collections.unmodifiableMap(copy);
    }
    /**
     * C#'s {@code internal} is assembly-scoped (visible to every namespace in
     * {@code Foundgine.Core}, including {@code Foundgine.Core.Execution}).
     * This port has no JPMS module boundary around the single {@code
     * foundgine-core} artifact, so the closest equivalent is {@code public}
     * rather than package-private (needed by {@code
     * com.foundgine.core.execution.ResultMaterializer}).
     */
    public List<SemanticResultNode> getChildren(RelationshipId relationshipId) {
        return children.computeIfAbsent(relationshipId, ignored -> new ArrayList<>());
    }
}

