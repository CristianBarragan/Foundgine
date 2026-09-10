package com.foundgine.core.semantic.authorization;
import com.foundgine.core.abstractions.*;
/** Provider-independent authorization policy for semantic requests. */
public interface ISemanticAuthorizationPolicy {
    boolean canAccessEntity(EntityId id);
    boolean canAccessField(EntityId entityId, FieldId fieldId);
    boolean canAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId);
    default boolean canWriteEntity(EntityId id){return false;}
    default boolean canWriteField(EntityId entityId, FieldId fieldId){return false;}
    default boolean canWriteRelationship(EntityId sourceEntityId, RelationshipId relationshipId){return false;}
    default AuthorizationPredicate getPredicate(EntityId id, AuthorizationOperation operation){return null;}
    default AuthorizationDecision getEntityAccess(EntityId id, AuthorizationOperation op){return op==AuthorizationOperation.READ?(canAccessEntity(id)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED):(canWriteEntity(id)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED);}
    default AuthorizationDecision getEntityAccess(EntityId id, AuthorizationOperation op, AuthorizationOperationName name){return getEntityAccess(id,op);}
    default AuthorizationDecision getFieldAccess(EntityId id, FieldId field, AuthorizationOperation op){return op==AuthorizationOperation.READ?(canAccessField(id,field)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED):(canWriteField(id,field)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED);}
    default AuthorizationDecision getRelationshipAccess(EntityId id, RelationshipId relationship, AuthorizationOperation op){return op==AuthorizationOperation.READ?(canAccessRelationship(id,relationship)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED):(canWriteRelationship(id,relationship)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED);}
}
