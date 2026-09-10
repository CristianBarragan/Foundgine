package com.foundgine.core.semantic.authorization;
import com.foundgine.core.abstractions.*;
/** Default unrestricted policy. */
public class AllowAllSemanticAuthorizationPolicy implements ISemanticAuthorizationPolicy {
 public boolean canAccessEntity(EntityId id){return true;} public boolean canAccessField(EntityId e,FieldId f){return true;} public boolean canAccessRelationship(EntityId e,RelationshipId r){return true;}
 public boolean canWriteEntity(EntityId id){return true;} public boolean canWriteField(EntityId e,FieldId f){return true;} public boolean canWriteRelationship(EntityId e,RelationshipId r){return true;}
 public AuthorizationPredicate getPredicate(EntityId e,AuthorizationOperation o){return null;}
 public AuthorizationDecision getEntityAccess(EntityId e,AuthorizationOperation o){return AuthorizationDecision.ALLOWED;}
 public AuthorizationDecision getEntityAccess(EntityId e,AuthorizationOperation o,AuthorizationOperationName n){return getEntityAccess(e,o);}
 public AuthorizationDecision getFieldAccess(EntityId e,FieldId f,AuthorizationOperation o){return AuthorizationDecision.ALLOWED;}
 public AuthorizationDecision getRelationshipAccess(EntityId e,RelationshipId r,AuthorizationOperation o){return AuthorizationDecision.ALLOWED;}
}
