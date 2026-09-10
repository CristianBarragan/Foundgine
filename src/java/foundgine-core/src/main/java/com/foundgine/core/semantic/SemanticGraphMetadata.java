package com.foundgine.core.semantic;
import com.foundgine.core.abstractions.*;
import java.util.List;
public final class SemanticGraphMetadata {
    private SemanticGraphMetadata() {}
    public record TraversalOrigin(EntityId sourceEntity, List<RelationshipId> path) { public TraversalOrigin { path=List.copyOf(path); } }
    public record IntentOrigin(String operation, String source) { public IntentOrigin(String operation) { this(operation,null); } }
    public enum ExpectedCardinality { UNKNOWN, ONE, MANY }
}
