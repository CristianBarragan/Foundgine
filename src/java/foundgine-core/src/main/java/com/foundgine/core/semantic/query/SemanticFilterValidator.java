package com.foundgine.core.semantic.query;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;

/** Validates semantic filters against the resolved semantic contract. */
public final class SemanticFilterValidator {
 private SemanticFilterValidator(){}
 public static void validate(SemanticFilterExpression filter,SemanticEntity entity,SemanticModel model){if(filter==null)return;visit(filter,entity,model::get);}
 public static void validate(SemanticFilterExpression filter,SemanticEntity entity,SemanticContractSnapshot contract){if(filter==null)return;visit(filter,entity,contract::get);}
 @FunctionalInterface private interface EntityGetter { SemanticEntity get(EntityId id); }
 private static void visit(SemanticFilterExpression e,SemanticEntity entity,EntityGetter get){
   if(e instanceof SemanticFieldFilter f){if(!declared(entity,f.field()))throw invalid("Entity '"+entity.name()+"' does not declare filter field '"+f.field()+"'.");if(!filterable(entity,f.field()))throw invalid("Field '"+entity.name()+"."+f.field()+"' is not filterable.");if(f.operator()==SemanticFilterOperator.IN&&f.value()==null)throw invalid("IN filter on '"+entity.name()+"."+f.field()+"' requires a value list.");if(f.value()!=null&&!f.field().equals(entity.identity().fieldId()))SemanticValueValidator.validate(f.value(),field(entity,f.field()),f.operator().toString());return;}
   if(e instanceof SemanticRelationshipFilter f){var r=entity.relationships().stream().filter(x->x.id().equals(f.relationship())).findFirst().orElseThrow(()->invalid("Entity '"+entity.name()+"' does not declare filter relationship '"+f.relationship()+"'."));visit(f.predicate(),get.get(r.target()),get);return;}
   if(e instanceof SemanticAggregateFilter f){var r=entity.relationships().stream().filter(x->x.id().equals(f.relationship())).findFirst().orElseThrow(()->invalid("Entity '"+entity.name()+"' does not declare aggregate filter relationship '"+f.relationship()+"'."));if(r.cardinality()!=RelationshipCardinality.MANY)throw invalid("Aggregate filters are only valid on collection relationships.");var target=get.get(r.target());if(f.predicate()!=null)visit(f.predicate(),target,get);if(f.aggregate()==SemanticFilterAggregate.COUNT){if(f.field()!=null)throw invalid("COUNT aggregate filters do not accept a target field.");}else{if(f.field()==null)throw invalid(f.aggregate()+" aggregate filters require a target field.");if(!declared(target,f.field()))throw invalid("Aggregate filter field '"+f.field()+"' is not defined on '"+target.name()+"'.");if(!aggregatable(target,f.field()))throw invalid("Field '"+target.name()+"."+f.field()+"' is not aggregatable.");if(f.value()!=null&&!f.field().equals(target.identity().fieldId()))SemanticValueValidator.validate(f.value(),field(target,f.field()),f.operator().toString());}return;}
   if(e instanceof SemanticAndFilter f){if(f.expressions().isEmpty())throw invalid("AND filter cannot be empty.");f.expressions().forEach(x->visit(x,entity,get));return;}
   if(e instanceof SemanticOrFilter f){if(f.expressions().isEmpty())throw invalid("OR filter cannot be empty.");f.expressions().forEach(x->visit(x,entity,get));return;}
   throw invalid("Unsupported semantic filter '"+e.getClass().getSimpleName()+"'.");
 }
 private static boolean declared(SemanticEntity e,FieldId id){return e.identity().fieldId().equals(id)||e.fields().stream().anyMatch(x->x.id().equals(id));}
 private static boolean filterable(SemanticEntity e,FieldId id){return e.identity().fieldId().equals(id)||e.fields().stream().anyMatch(x->x.id().equals(id)&&(x.capabilities()&SemanticFieldCapabilities.FILTERABLE)!=0);}
 private static boolean aggregatable(SemanticEntity e,FieldId id){return e.identity().fieldId().equals(id)||e.fields().stream().anyMatch(x->x.id().equals(id)&&(x.capabilities()&SemanticFieldCapabilities.AGGREGATABLE)!=0);}
 private static SemanticField field(SemanticEntity e,FieldId id){return e.fields().stream().filter(x->x.id().equals(id)).findFirst().orElse(new SemanticField(e.identity().fieldId(),e.identity().name(),Object.class));}
 private static IllegalStateException invalid(String m){return new IllegalStateException("Invalid semantic filter: "+m);}
}
