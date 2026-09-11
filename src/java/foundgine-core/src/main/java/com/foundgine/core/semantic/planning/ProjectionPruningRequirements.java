package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Derives fields that must remain available to evaluate a semantic node. */
public final class ProjectionPruningRequirements {
    private ProjectionPruningRequirements() {}
    public static Set<FieldId> requiredRootFields(SemanticPlanNode node){Objects.requireNonNull(node);var required=new LinkedHashSet<>(node.fields());var options=node.queryOptions();if(options==null)return required;collectFilterFields(options.filter(),required);for(var order:options.effectiveOrder())if(order.isRootField()&&!order.isAggregate())required.add(order.field());return required;}
    private static void collectFilterFields(SemanticFilterExpression filter,Set<FieldId> required){
        if(filter instanceof SemanticFieldFilter f){required.add(f.field());return;}
        if(filter instanceof SemanticRelationshipFilter r){collectFilterFields(r.predicate(),required);return;}
        if(filter instanceof SemanticAggregateFilter a){if(a.field()!=null)required.add(a.field());if(a.predicate()!=null)collectFilterFields(a.predicate(),required);return;}
        if(filter instanceof SemanticAndFilter a){a.expressions().forEach(x->collectFilterFields(x,required));return;}
        if(filter instanceof SemanticOrFilter o)o.expressions().forEach(x->collectFilterFields(x,required));
    }
}
