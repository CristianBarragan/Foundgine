package com.foundgine.core.semantic.planning;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationResult;
import com.foundgine.core.semantic.ir.*;
import java.util.*;
/** Converts canonical Semantic IR into provider-neutral SemanticPlan. */
public final class Planner implements IPlanner {
 @Override public SemanticPlan plan(SemanticOperation operation){Objects.requireNonNull(operation);return new SemanticPlan(build(operation.root(),new HashSet<>(),true));}
 private SemanticPlanNode build(SemanticReadNode n,Set<Integer> visited,boolean root){
  if(!visited.add(n.id()))throw new IllegalArgumentException("Semantic operation contains a cycle or duplicate node at "+n.id()+".");
  if(!root&&n.viaRelationship()==null&&n.viaConnection()==null)throw new IllegalArgumentException("Non-root semantic node "+n.id()+" must specify the relationship or connection used to reach it.");
  if(n.viaRelationship()!=null&&n.viaConnection()!=null)throw new IllegalArgumentException("Semantic node "+n.id()+" cannot specify both a relationship and a connection.");
  if(root&&(n.viaRelationship()!=null||n.viaConnection()!=null))throw new IllegalArgumentException("Root semantic node "+n.id()+" cannot specify a parent edge.");
  var op=root?ExecutionOperation.SCAN:(n.viaConnection()!=null?ExecutionOperation.TRAVERSE_CONNECTION:ExecutionOperation.TRAVERSE);
  var children=n.children().stream().map(c->build(c,visited,false)).toList();
  return new SemanticPlanNode(n.id(),op,n.entityId(),n.fields(),n.viaRelationship(),n.viaConnection(),children,root?n.queryOptions():null,n.authorization(),null,RelationshipTraversalMode.DEFAULT,-1,AggregateExecutionStrategy.DEFAULT);
 }
}
