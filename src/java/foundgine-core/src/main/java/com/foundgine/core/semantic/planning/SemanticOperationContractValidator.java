package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Validates that canonical semantic IR belongs to the trusted frozen contract used for planning. */
final class SemanticOperationContractValidator {
    private SemanticOperationContractValidator() {}
    static void validate(SemanticOperation operation,SemanticContractSnapshot contract){Objects.requireNonNull(operation);Objects.requireNonNull(contract);validateNode(operation.root(),contract,new HashSet<>(),true);}
    private static void validateNode(SemanticReadNode node,SemanticContractSnapshot contract,Set<Integer> visited,boolean root){
        if(!visited.add(node.id()))throw new IllegalStateException("Semantic operation contains a cycle or duplicate node at "+node.id()+".");var entity=contract.get(node.entityId());if(root&&(node.viaRelationship()!=null||node.viaConnection()!=null))throw new IllegalStateException("Root semantic node "+node.id()+" cannot specify a parent edge.");if(!root&&node.viaRelationship()==null&&node.viaConnection()==null)throw new IllegalStateException("Non-root semantic node "+node.id()+" must specify the relationship or connection used to reach it.");if(node.viaRelationship()!=null&&node.viaConnection()!=null)throw new IllegalStateException("Semantic node "+node.id()+" cannot specify both a relationship and a connection.");
        var fields=new LinkedHashSet<FieldId>(node.fields());fields.addAll(node.requiredFields());for(var field:fields)ensureField(entity,field,"select");if(root&&node.queryOptions()!=null){validateFilter(node.queryOptions().filter(),entity,contract);validateOrder(node.queryOptions().effectiveOrder(),entity,contract);}for(var child:node.children()){if(child.viaRelationship()!=null){var rel=ensureRelationship(entity,child.viaRelationship());if(!rel.target().equals(child.entityId()))throw new IllegalStateException("Semantic operation node "+child.id()+" targets '"+child.entityId()+"', but relationship '"+rel.name()+"' targets '"+rel.target()+"'.");}validateNode(child,contract,visited,false);}
    }
    private static void validateFilter(SemanticFilterExpression filter,SemanticEntity entity,SemanticContractSnapshot contract){if(filter==null)return;if(filter instanceof SemanticFieldFilter f){ensureField(entity,f.field(),"filter");return;}if(filter instanceof SemanticRelationshipFilter r){var rel=ensureRelationship(entity,r.relationship());validateFilter(r.predicate(),contract.get(rel.target()),contract);return;}if(filter instanceof SemanticAggregateFilter a){var rel=ensureRelationship(entity,a.relationship());if(a.field()!=null)ensureField(contract.get(rel.target()),a.field(),"aggregate filter");validateFilter(a.predicate(),contract.get(rel.target()),contract);return;}if(filter instanceof SemanticAndFilter a){a.expressions().forEach(x->validateFilter(x,entity,contract));return;}if(filter instanceof SemanticOrFilter o){o.expressions().forEach(x->validateFilter(x,entity,contract));return;}throw new IllegalStateException("Unsupported semantic filter '"+filter.getClass().getSimpleName()+"'.");}
    private static void validateOrder(List<SemanticOrderTerm> order,SemanticEntity root,SemanticContractSnapshot contract){for(var term:order){var entity=root;for(var relationshipId:term.effectivePath()){var rel=ensureRelationship(entity,relationshipId);entity=contract.get(rel.target());}ensureField(entity,term.field(),"order");}}
    private static SemanticRelationship ensureRelationship(SemanticEntity entity,RelationshipId id){return entity.relationships().stream().filter(x->x.id().equals(id)).findFirst().orElseThrow(()->new IllegalStateException("Semantic operation references relationship '"+id+"' not declared on '"+entity.name()+"'."));}
    private static void ensureField(SemanticEntity entity,FieldId id,String context){if(entity.identity().fieldId().equals(id)||entity.fields().stream().anyMatch(x->x.id().equals(id)))return;throw new IllegalStateException("Semantic operation references unknown "+context+" field '"+id+"' on '"+entity.name()+"'.");}
}
