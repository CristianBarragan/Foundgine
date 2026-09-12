package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.ir.graph.*;
import java.util.*;

/** Applies authorization to resolved semantic graphs/operations before planning. */
public final class SemanticAuthorizer {
 private final ISemanticAuthorizationPolicy policy;
 public SemanticAuthorizer(ISemanticAuthorizationPolicy policy){this.policy=Objects.requireNonNull(policy);}

 public SemanticGraph authorize(SemanticGraph graph){
  Objects.requireNonNull(graph); if(graph.nodes().isEmpty())return graph; var authorized=new SemanticGraph();authorized.setOptions(graph.options());var map=new HashMap<Integer,SemanticGraph.SemanticGraphNode>();
  for(var source:graph.nodes()){
   SemanticGraph.SemanticGraphNode parent=null;if(source.parentId()!=null){parent=map.get(source.parentId());if(parent==null)continue;}
   var fieldDecisions=source.fields().stream().distinct().map(f->new AbstractMap.SimpleEntry<>(f,policy.getFieldAccess(source.entityId(),f,AuthorizationOperation.READ))).toList();
   var fields=fieldDecisions.stream().filter(x->x.getValue().isAllowed()).map(Map.Entry::getKey).toList();
   var fieldAuth=SemanticAuthorizationCapabilityComposition.compose(fieldDecisions.stream().filter(x->x.getValue().isAllowed()).map(Map.Entry::getValue).toList());
   var authorization=AuthorizationDecision.combine(policy.getEntityAccess(source.entityId(),AuthorizationOperation.READ),predicateDecision(policy.getPredicate(source.entityId(),AuthorizationOperation.READ)));
   authorization=AuthorizationDecision.combine(authorization,fieldAuth); if(!authorization.isAllowed()){if(source.parentId()==null)throw new SemanticAuthorizationException("Access denied for entity '"+source.entityId()+"'.");continue;}
   if(source.viaRelationship()!=null){if(source.parentId()==null||graph.nodes().stream().noneMatch(n->n.id()==source.parentId()))throw new IllegalArgumentException("Graph node "+source.id()+" has relationship '"+source.viaRelationship()+"' but no valid parent.");var parentEntity=graph.nodes().stream().filter(n->n.id()==source.parentId()).findFirst().orElseThrow().entityId();var rd=policy.getRelationshipAccess(parentEntity,source.viaRelationship(),AuthorizationOperation.READ);if(!rd.isAllowed())continue;authorization=AuthorizationDecision.combine(authorization,rd);}
   if(source.authorization()!=null&&!source.authorization().equals(authorization.predicate()))authorization=AuthorizationDecision.combine(authorization,predicateDecision(source.authorization()));
   SemanticGraph.SemanticGraphNode node;if(source.parentId()==null)node=authorized.addRoot(source.entityId(),fields,authorization.predicate());else if(source.viaRelationship()!=null)node=authorized.add(source.entityId(),source.viaRelationship(),parent,fields,authorization.predicate());else if(source.viaConnection()!=null)node=authorized.addConnection(source.entityId(),source.viaConnection(),parent,fields,authorization.predicate());else throw new IllegalArgumentException("Graph node "+source.id()+" has a parent but no semantic edge.");map.put(source.id(),node);
  } return authorized;
 }
 public SemanticOperationGraph authorize(SemanticContractSnapshot c,SemanticOperationGraph g){return authorizeGraphWithEvidence(c,g).graph();}
 public SemanticOperationGraphAuthorizationResult authorizeGraphWithEvidence(SemanticContractSnapshot c,SemanticOperationGraph g){Objects.requireNonNull(c);Objects.requireNonNull(g);var result=authorizeWithEvidence(c,g.toOperation());return new SemanticOperationGraphAuthorizationResult(SemanticOperationGraph.create(result.operation()),result.evidence());}
 public SemanticOperation authorize(SemanticContractSnapshot c,SemanticOperation op){return authorizeWithEvidence(c,op).operation();}
 public SemanticAuthorizationResult authorizeWithEvidence(SemanticContractSnapshot c,SemanticOperation op){Objects.requireNonNull(c);Objects.requireNonNull(op);SemanticAuthorizationContractValidator.validate(c,op);var authorized=authorize(op);return new SemanticAuthorizationResult(authorized,SemanticAuthorizationEvidence.create(c,authorized));}
 public SemanticOperation authorize(SemanticOperation op){Objects.requireNonNull(op);var root=authorizeNode(op.root(),true);if(root==null)throw new SemanticAuthorizationException("Access denied for entity '"+op.root().entityId()+"'.");return new SemanticOperation(root);}
 private SemanticReadNode authorizeNode(SemanticReadNode n,boolean root){
  var effective=AuthorizationDecision.combine(policy.getEntityAccess(n.entityId(),AuthorizationOperation.READ),predicateDecision(policy.getPredicate(n.entityId(),AuthorizationOperation.READ)));if(!effective.isAllowed()){if(root)throw new SemanticAuthorizationException("Access denied for entity '"+n.entityId()+"'.");return null;}
  var fields=new ArrayList<FieldId>();for(var f:new LinkedHashSet<>(n.fields())){var d=policy.getFieldAccess(n.entityId(),f,AuthorizationOperation.READ);if(d.isAllowed()){fields.add(f);effective=AuthorizationDecision.combine(effective,d);}}
  if(n.authorization()!=null&&!n.authorization().equals(effective.predicate()))effective=AuthorizationDecision.combine(effective,predicateDecision(n.authorization()));if(!effective.isAllowed()){if(root)throw new SemanticAuthorizationException("Authorization constraints denied semantic node '"+n.id()+"'.");return null;}
  var children=new ArrayList<SemanticReadNode>();for(var child:n.children()){
   if(child.viaRelationship()!=null){var rd=policy.getRelationshipAccess(n.entityId(),child.viaRelationship(),AuthorizationOperation.READ);if(!rd.isAllowed())continue;var ca=authorizeNode(child,false);if(ca==null)continue;var combined=AuthorizationDecision.combine(rd,ca.authorization()==null?AuthorizationDecision.ALLOWED:predicateDecision(ca.authorization()));children.add(ca.withAuthorization(combined.predicate()));}
   else {var ca=authorizeNode(child,false);if(ca!=null)children.add(ca);}
  }
  return new SemanticReadNode(n.id(),n.entityId(),fields,n.viaRelationship(),n.viaConnection(),children,n.queryOptions(),effective.predicate(),n.requiredFields());
 }
 private static AuthorizationDecision predicateDecision(AuthorizationPredicate p){return p==null?AuthorizationDecision.ALLOWED:AuthorizationDecision.conditional(p);}
}
