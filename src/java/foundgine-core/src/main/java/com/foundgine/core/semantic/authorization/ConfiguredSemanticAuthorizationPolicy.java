package com.foundgine.core.semantic.authorization;
import com.foundgine.core.abstractions.*; import java.util.*;
/** Authorization policy backed entirely by application configuration. */
public final class ConfiguredSemanticAuthorizationPolicy implements ISemanticAuthorizationPolicy {
 private final SemanticAuthorizationConfiguration configuration;private final SemanticAuthorizationContext context;
 public ConfiguredSemanticAuthorizationPolicy(SemanticAuthorizationConfiguration c,SemanticAuthorizationContext x){configuration=Objects.requireNonNull(c);context=Objects.requireNonNull(x);}
 public boolean canAccessEntity(EntityId e){return configuration.canAccessEntity(context,e,AuthorizationOperation.READ);} public boolean canAccessField(EntityId e,FieldId f){return configuration.canAccessField(context,e,f,AuthorizationOperation.READ);} public boolean canAccessRelationship(EntityId e,RelationshipId r){return configuration.canAccessRelationship(context,e,r,AuthorizationOperation.READ);}
 public boolean canWriteEntity(EntityId e){return configuration.canAccessEntity(context,e,AuthorizationOperation.WRITE);} public boolean canWriteField(EntityId e,FieldId f){return configuration.canAccessField(context,e,f,AuthorizationOperation.WRITE);} public boolean canWriteRelationship(EntityId e,RelationshipId r){return configuration.canAccessRelationship(context,e,r,AuthorizationOperation.WRITE);}
 public AuthorizationPredicate getPredicate(EntityId e,AuthorizationOperation o){return configuration.getPredicate(context,e,o);}
 public AuthorizationDecision getEntityAccess(EntityId e,AuthorizationOperation o){return o==AuthorizationOperation.READ?(canAccessEntity(e)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED):(canWriteEntity(e)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED);}
 public AuthorizationDecision getEntityAccess(EntityId e,AuthorizationOperation o,AuthorizationOperationName n){var coarse=getEntityAccess(e,o);if(!coarse.isAllowed())return coarse;return AuthorizationDecision.combine(coarse,Optional.ofNullable(configuration.getOperationDecision(context,e,o,n)).orElse(AuthorizationDecision.ALLOWED));}
 public AuthorizationDecision getFieldAccess(EntityId e,FieldId f,AuthorizationOperation o){return o==AuthorizationOperation.READ?(canAccessField(e,f)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED):(canWriteField(e,f)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED);}
 public AuthorizationDecision getRelationshipAccess(EntityId e,RelationshipId r,AuthorizationOperation o){return o==AuthorizationOperation.READ?(canAccessRelationship(e,r)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED):(canWriteRelationship(e,r)?AuthorizationDecision.ALLOWED:AuthorizationDecision.DENIED);}
}
