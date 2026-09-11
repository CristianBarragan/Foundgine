package com.foundgine.core.semantic.planning.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.mutation.SemanticMutationPlan;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Applies semantic write/read authorization to provider-independent mutation plans. */
public final class MutationAuthorizer {
    private final MutationSchema schema; private final ISemanticAuthorizationPolicy policy;
    public MutationAuthorizer(MutationSchema schema, ISemanticAuthorizationPolicy policy){this.schema=Objects.requireNonNull(schema);this.policy=Objects.requireNonNull(policy);}

    public MutationPlan authorize(MutationPlan plan){Objects.requireNonNull(plan);plan.operations().forEach(this::authorize);return plan;}
    public MutationBatchPlan authorize(MutationBatchPlan plan){Objects.requireNonNull(plan);plan.operations().forEach(this::authorize);return plan;}
    public SemanticMutationPlan authorize(SemanticMutationPlan plan){
        Objects.requireNonNull(plan); for(var op:plan.operations()){var entity=schema.getEntity(op.entity());require(policy.getEntityAccess(entity.id(),AuthorizationOperation.WRITE),"write entity '"+entity.name()+"'");
            for(var field:op.fields()){if(!entity.fields().containsKey(field.field()))throw new IllegalStateException("Mutation field '"+field.field().value()+"' is not registered on '"+entity.name()+"'.");require(policy.getFieldAccess(entity.id(),field.field(),AuthorizationOperation.WRITE),"write field '"+entity.name()+"."+field.field().value()+"'");}
            for(var field:op.conflictFields()){if(!entity.fields().containsKey(field))throw new IllegalStateException("Conflict field '"+field.value()+"' is not registered on '"+entity.name()+"'.");require(policy.getFieldAccess(entity.id(),field,AuthorizationOperation.WRITE),"write conflict field '"+entity.name()+"."+field.value()+"'");}
            for(var field:op.returnFields()){if(!entity.fields().containsKey(field))throw new IllegalStateException("Return field '"+field.value()+"' is not registered on '"+entity.name()+"'.");require(policy.getFieldAccess(entity.id(),field,AuthorizationOperation.READ),"read return field '"+entity.name()+"."+field.value()+"'");}
            validateFilter(op.filter(),entity);
        } return plan;
    }
    private void authorize(MutationOperation op){var entity=op.entity();require(policy.getEntityAccess(entity.id(),AuthorizationOperation.WRITE),"write entity '"+entity.name()+"'");
        for(var field:op.fields()){var fieldId=entity.fields().entrySet().stream().filter(e->Objects.equals(e.getValue(),field.column())).map(Map.Entry::getKey).findFirst().orElseThrow(()->new IllegalStateException("Mutation field column '"+field.column().value()+"' has no semantic field mapping on '"+entity.name()+"'."));require(policy.getFieldAccess(entity.id(),fieldId,AuthorizationOperation.WRITE),"write field '"+entity.name()+"."+fieldId.value()+"'");}
        if(op.returnFields()!=null)for(var field:op.returnFields())require(policy.getFieldAccess(entity.id(),field,AuthorizationOperation.READ),"read return field '"+entity.name()+"."+field.value()+"'");validateFilter(op.filter(),entity);
    }
    private void validateFilter(SemanticFilterExpression filter,MutationEntitySchema entity){if(filter==null)return;
        if(filter instanceof SemanticFieldFilter f){require(policy.getFieldAccess(entity.id(),f.field(),AuthorizationOperation.READ),"filter on field '"+entity.name()+"."+f.field().value()+"'");return;}
        if(filter instanceof SemanticRelationshipFilter r){require(policy.getRelationshipAccess(entity.id(),r.relationship(),AuthorizationOperation.READ),"filter through relationship '"+r.relationship().value()+"'");var rel=schema.getRelationship(r.relationship());validateFilter(r.predicate(),schema.getEntity(rel.target()));return;}
        if(filter instanceof SemanticAggregateFilter a){require(policy.getRelationshipAccess(entity.id(),a.relationship(),AuthorizationOperation.READ),"aggregate filter through relationship '"+a.relationship().value()+"'");if(a.field()!=null){var rel=schema.getRelationship(a.relationship());require(policy.getFieldAccess(rel.target(),a.field(),AuthorizationOperation.READ),"aggregate filter field '"+a.field().value()+"'");}return;}
        if(filter instanceof SemanticAndFilter a){a.expressions().forEach(x->validateFilter(x,entity));return;}
        if(filter instanceof SemanticOrFilter o){o.expressions().forEach(x->validateFilter(x,entity));return;}
        throw new UnsupportedOperationException("Mutation authorization does not support filter '"+filter.getClass().getSimpleName()+"'.");
    }
    private static void require(AuthorizationDecision decision,String resource){if(!decision.isAllowed())throw new SemanticAuthorizationException("Access denied for "+resource+".");}
}
