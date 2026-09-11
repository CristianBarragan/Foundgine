package com.foundgine.core.semantic.authorization;
import com.foundgine.core.abstractions.*; import com.foundgine.core.semantic.*; import java.util.*;
/** Builds provider-independent descriptive capability information; never grants authority. */
public final class SemanticAuthorizationCapabilityDiscovery {
 private SemanticAuthorizationCapabilityDiscovery(){}
 public static SemanticAuthorizationCapabilities describe(SemanticModel model,ISemanticAuthorizationPolicy policy){
  Objects.requireNonNull(model);Objects.requireNonNull(policy);
  return new SemanticAuthorizationCapabilities(model.entities().stream().sorted(Comparator.comparing(SemanticEntity::name,String.CASE_INSENSITIVE_ORDER)).map(e->describeEntity(model,policy,e)).toList());
 }
 private static SemanticAuthorizationCapability describeEntity(SemanticModel m,ISemanticAuthorizationPolicy p,SemanticEntity e){
  var read=effective(p.getEntityAccess(e.id(),AuthorizationOperation.READ),predicateDecision(p.getPredicate(e.id(),AuthorizationOperation.READ)));
  var write=effective(p.getEntityAccess(e.id(),AuthorizationOperation.WRITE),predicateDecision(p.getPredicate(e.id(),AuthorizationOperation.WRITE)));
  var fields=e.fields().stream().sorted(Comparator.comparing(SemanticField::name,String.CASE_INSENSITIVE_ORDER)).map(f->new SemanticFieldAuthorizationCapability(f.id(),f.name(),effective(read,p.getFieldAccess(e.id(),f.id(),AuthorizationOperation.READ)),effective(write,p.getFieldAccess(e.id(),f.id(),AuthorizationOperation.WRITE)))).toList();
  var rels=e.relationships().stream().sorted(Comparator.comparing(SemanticRelationship::name,String.CASE_INSENSITIVE_ORDER)).map(r->{var target=m.get(r.target());return new SemanticRelationshipAuthorizationCapability(r.id(),r.name(),r.target(),effective(read,p.getEntityAccess(target.id(),AuthorizationOperation.READ),p.getRelationshipAccess(e.id(),r.id(),AuthorizationOperation.READ)),effective(write,p.getEntityAccess(target.id(),AuthorizationOperation.WRITE),p.getRelationshipAccess(e.id(),r.id(),AuthorizationOperation.WRITE)));}).toList();
  return new SemanticAuthorizationCapability(e.id(),e.name(),read,write,fields,rels);
 }
 private static AuthorizationDecision predicateDecision(AuthorizationPredicate p){return p==null?AuthorizationDecision.ALLOWED:AuthorizationDecision.conditional(p);}
 private static AuthorizationDecision effective(AuthorizationDecision... d){return SemanticAuthorizationCapabilityComposition.compose(d);}
}
