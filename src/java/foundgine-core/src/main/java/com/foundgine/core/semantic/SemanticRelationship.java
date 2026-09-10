package com.foundgine.core.semantic;
import com.foundgine.core.abstractions.*;
import java.util.*;
public record SemanticRelationship(RelationshipId id, String name, EntityId target, RelationshipCardinality cardinality, List<SemanticAlias> aliases) {
    public SemanticRelationship { aliases = aliases == null ? List.of() : List.copyOf(aliases); }
    public SemanticRelationship(RelationshipId id, String name, EntityId target, RelationshipCardinality cardinality) { this(id,name,target,cardinality,List.of()); }
    public List<SemanticAlias> effectiveAliases() { return aliases; }
}
