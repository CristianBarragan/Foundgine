package com.foundgine.core.semantic.authorization;
import com.foundgine.core.abstractions.*;
public record SemanticRelationshipAuthorizationCapability(RelationshipId relationshipId,String name,EntityId targetEntityId,AuthorizationDecision read,AuthorizationDecision write){}
