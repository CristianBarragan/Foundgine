package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.execution.security.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.providers.storage.sql.query.*;
import com.foundgine.providers.storage.sql.security.SqlSecurityConformance;
import java.util.*;
import com.fasterxml.jackson.databind.JsonNode;

/** Compiles provider-independent Execution IR into parameterized SQL. */
public final class SqlCompiler implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {
    private final IMetadataProvider metadata;
    public SqlCompiler(IMetadataProvider metadata) { this.metadata=Objects.requireNonNull(metadata,"metadata"); }

    public Collection<String> preservedSecurityInvariants() { return List.of(
            SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.RUNTIME_AUTHORIZATION,
            SecurityInvariantIds.FIELD_VISIBILITY, SecurityInvariantIds.RELATIONSHIP_VISIBILITY,
            SecurityInvariantIds.PARAMETERIZED_VALUES, SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION); }

    @Override public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
        Objects.requireNonNull(ir); Objects.requireNonNull(plan);
        if (!(plan instanceof SqlPlan sqlPlan)) throw new IllegalArgumentException("Expected a SqlPlan.");
        var result=SqlSecurityConformance.verify(ir,sqlPlan);
        return new ProviderSecurityConformanceResult(sqlPlan.provider(),result.required(),result.satisfied(),result.violations());
    }
    public SqlPlan compile(SemanticPlan plan) { return compile(ExecutionIRCompiler.compile(plan)); }

    @Override public SqlPlan compile(ExecutionIR ir) {
        Objects.requireNonNull(ir);
        List<NodeOccurrence> occurrences=new ArrayList<>(); collect(ir.root(),occurrences,null);
        Map<Integer,String> aliases=new LinkedHashMap<>(); for(var o:occurrences) aliases.put(o.node().id(),"t"+o.node().id());
        List<String> select=new ArrayList<>(); List<SqlColumnBinding> bindings=new ArrayList<>();
        List<SqlAuthorizationPredicate> authorization=occurrences.stream().filter(x->x.node().authorization()!=null)
                .map(x->new SqlAuthorizationPredicate(x.node().id(),x.node().authorization())).toList();
        for(var occurrence:occurrences){
            var entity=metadata.getEntity(occurrence.node().entityId());
            if(occurrence.node().fields().isEmpty()) throw new IllegalStateException("Execution node "+occurrence.node().id()+" selects no fields after semantic authorization.");
            for(var fieldId:occurrence.node().fields()) addFieldSelection(occurrence.node(),entity,fieldId,aliases,select,bindings);
        }
        if(select.isEmpty()) throw new IllegalStateException("The execution IR selects no fields.");
        List<SqlParameterBinding> parameters=new ArrayList<>(); NodeOccurrence root=occurrences.get(0);
        var rootEntity=metadata.getEntity(root.node().entityId()); var rootOptions=root.node().queryOptions();
        List<SemanticOrderTerm> requestedOrder=rootOptions==null?List.of():rootOptions.effectiveOrder();
        boolean hasCursor=rootOptions!=null && rootOptions.limit()!=null && rootOptions.limit()>0 && rootOptions.after()!=null;
        boolean forward=rootOptions!=null && rootOptions.limit()!=null && rootOptions.limit()>0 && rootOptions.offset()==null;
        if(rootOptions!=null && rootOptions.after()!=null && !(rootOptions.limit()!=null && rootOptions.limit()>0)) throw new IllegalStateException("Cursor pagination requires a positive first/limit value.");
        if(rootOptions!=null && rootOptions.after()!=null && rootOptions.offset()!=null) throw new UnsupportedOperationException("Cursor pagination cannot be combined with offset pagination.");
        if(rootOptions!=null && ((rootOptions.limit()!=null&&rootOptions.limit()<0)||(rootOptions.offset()!=null&&rootOptions.offset()<0))) throw new IllegalArgumentException("Query limit and offset must be non-negative.");
        SqlPaginationPlan pagination=null; List<SemanticOrderTerm> cursorOrder=forward?buildCursorOrder(rootEntity,requestedOrder):List.of();
        List<ResolvedOrderTerm> resolved=new ArrayList<>(); List<SemanticOrderTerm> orderTerms=forward?cursorOrder:requestedOrder;
        for(var term:orderTerms){
            ExecutionIRNode orderNode; EntityMetadata orderEntity; FieldMetadata field;
            if(term.isAggregate()){
                if(term.effectivePath().size()!=1) throw new UnsupportedOperationException("Collection aggregation currently supports one relationship hop.");
                orderNode=resolveOrderParentNode(root.node(),term.effectivePath());
                orderEntity=metadata.getEntity(metadata.getRelationship(term.effectivePath().get(0)).target());
            } else { orderNode=resolveOrderNode(root.node(),term.effectivePath()); orderEntity=metadata.getEntity(orderNode.entityId()); }
            field=orderEntity.effectiveFields().stream().filter(x->x.id().equals(term.field())).findFirst()
                    .orElseThrow(()->new IllegalArgumentException("Unknown order field '"+term.field()+"' on '"+orderEntity.name()+"'."));
            if(!term.isAggregate()&&field.column()==null) throw new IllegalArgumentException("Order field '"+orderEntity.name()+"."+field.name()+"' has no storage column mapping.");
            if(term.isAggregate()&&term.effectivePath().isEmpty()) throw new IllegalArgumentException("Aggregate ordering requires a collection relationship path.");
            resolved.add(new ResolvedOrderTerm(term,orderNode,orderEntity,aliases.get(orderNode.id()),field));
            if(forward){
                List<SqlCursorBinding> cursorBindings=pagination==null?new ArrayList<>():new ArrayList<>(pagination.cursorValues());
                if(term.isAggregate()) addHiddenAggregateCursorSelection(term,orderNode,orderEntity,field,aliases,select,bindings,cursorBindings);
                else addHiddenCursorSelection(orderNode,orderEntity,field,term.direction(),aliases,select,bindings,cursorBindings);
                pagination=new SqlPaginationPlan(rootOptions.limit(),cursorBindings,rootOptions.after());
            }
        }
        StringBuilder sql=new StringBuilder("SELECT ").append(String.join(", ",select));
        sql.append(" FROM ").append(quoteStorageName(rootEntity.effectiveStorageName())).append(" ").append(quoteIdentifier(aliases.get(root.node().id())));
        for(var occurrence:occurrences.subList(1,occurrences.size())){
            if(occurrence.node().viaRelationship()==null) throw new IllegalStateException("Node "+occurrence.node().id()+" has no relationship to its parent.");
            var relationship=metadata.getRelationship(occurrence.node().viaRelationship());
            var parent=occurrences.stream().filter(x->Objects.equals(x.node().id(),occurrence.parentId())).findFirst().orElseThrow().node();
            String left=renderJoinColumn(relationship.sourceKey(),parent,occurrence.node(),aliases), right=renderJoinColumn(relationship.targetKey(),parent,occurrence.node(),aliases);
            sql.append(" INNER JOIN ").append(quoteStorageName(metadata.getEntity(occurrence.node().entityId()).effectiveStorageName())).append(" ").append(quoteIdentifier(aliases.get(occurrence.node().id()))).append(" ON ").append(left).append(" = ").append(right);
        }
        String where=SemanticQuerySqlWriter.writeWhere(rootOptions==null?null:rootOptions.filter(),rootEntity,aliases.get(root.node().id()),parameters,metadata,root.node().aggregateExecutionStrategy());
        for(var occurrence:occurrences){ if(occurrence.node().authorization()==null) continue; var entity=metadata.getEntity(occurrence.node().entityId()); String p=SqlAuthorizationWriter.write(occurrence.node().authorization(),entity,aliases.get(occurrence.node().id()),parameters); where=where==null||where.isBlank()?p:"("+where+") AND ("+p+")"; }
        if(hasCursor){
            List<JsonNode> values=CursorCodec.decode(rootOptions.after());
            if(values.size()!=cursorOrder.size()) throw new IllegalArgumentException("The pagination cursor contains "+values.size()+" values, but the current ordering requires "+cursorOrder.size()+".");
            String seek=buildSeekPredicate(resolved,values,parameters); where=where==null||where.isBlank()?seek:"("+where+") AND ("+seek+")";
        }
        if(where!=null&&!where.isBlank()) sql.append(" WHERE ").append(where);
        String order=writeResolvedOrder(resolved);
        if(forward){
            sql.append(" ").append(order); parameters.add(new SqlParameterBinding("__fg_limit",null,null,ExecutionContextKeys.PAGINATION_LIMIT,null)); sql.append(" LIMIT @__fg_limit");
        } else {
            if(!order.isBlank()) sql.append(" ").append(order);
            if(rootOptions!=null&&rootOptions.offset()!=null&&rootOptions.limit()==null) sql.append(" LIMIT -1");
            else if(rootOptions!=null&&rootOptions.limit()!=null){ parameters.add(new SqlParameterBinding("__fg_limit",null,null,ExecutionContextKeys.PAGINATION_LIMIT,null)); sql.append(" LIMIT @__fg_limit"); }
            if(rootOptions!=null&&rootOptions.offset()!=null){ parameters.add(new SqlParameterBinding("__fg_offset",null,null,ExecutionContextKeys.PAGINATION_OFFSET,null)); sql.append(" OFFSET @__fg_offset"); }
        }
        SqlPlan plan=new SqlPlan(sql.toString(),bindings,parameters,pagination,authorization); ExecutionIRBoundary.bindProviderPlan(ir,plan);
        if(!ir.requiredSecurityInvariants().isEmpty()) SqlSecurityConformance.ensureSatisfied(ir,plan); return plan;
    }

    private static void addFieldSelection(ExecutionIRNode node,EntityMetadata entity,FieldId fieldId,Map<Integer,String> aliases,List<String> select,List<SqlColumnBinding> bindings){
        FieldMetadata field=entity.effectiveFields().stream().filter(x->x.id().equals(fieldId)).findFirst().orElseThrow(()->new IllegalArgumentException("Unknown field '"+fieldId+"' on entity '"+entity.name()+"'."));
        if(field.column()==null) throw new IllegalArgumentException("Field '"+entity.name()+"."+field.name()+"' has no storage column mapping.");
        ColumnMetadata column=entity.columns().stream().filter(x->x.id().equals(field.column().columnId())).findFirst().orElseThrow(()->new IllegalArgumentException("Field '"+entity.name()+"."+field.name()+"' references a missing column '"+field.column().columnId()+"'."));
        String result="__fg_"+node.id()+"_"+field.name(); select.add(quoteIdentifier(aliases.get(node.id()))+"."+quoteIdentifier(column.effectiveStorageName())+" AS "+quoteIdentifier(result)); bindings.add(new SqlColumnBinding(result,entity.entityId(),field.id(),column.effectiveStorageName(),node.id()));
    }
    private void addHiddenAggregateCursorSelection(SemanticOrderTerm term,ExecutionIRNode node,EntityMetadata entity,FieldMetadata field,Map<Integer,String> aliases,List<String> select,List<SqlColumnBinding> bindings,List<SqlCursorBinding> cursorBindings){
        String expression=buildAggregateReference(term,node,entity,field,aliases); String result="__fg_cursor_agg_"+node.id()+"_"+field.name()+"_"+term.aggregate(); select.add(expression+" AS "+quoteIdentifier(result)); Class<?> type=term.aggregate()==SemanticOrderAggregate.COUNT?Long.class:field.clrType(); var target=metadata.getRelationship(term.effectivePath().get(0)).target(); bindings.add(new SqlColumnBinding(result,target,field.id(),term.aggregate().toString(),node.id(),true)); cursorBindings.add(new SqlCursorBinding(result,target,field.id(),type,term.direction()));
    }
    private static void addHiddenCursorSelection(ExecutionIRNode node,EntityMetadata entity,FieldMetadata field,SemanticSortDirection direction,Map<Integer,String> aliases,List<String> select,List<SqlColumnBinding> bindings,List<SqlCursorBinding> cursorBindings){
        if(field.column()==null) throw new IllegalArgumentException("Order field '"+entity.name()+"."+field.name()+"' has no storage column mapping.");
        ColumnMetadata column=entity.columns().stream().filter(x->x.id().equals(field.column().columnId())).findFirst().orElseThrow(); String result="__fg_cursor_"+node.id()+"_"+field.name(); SqlColumnBinding existing=bindings.stream().filter(x->x.nodeId()==node.id()&&x.fieldId().equals(field.id())).findFirst().orElse(null);
        if(existing==null){ select.add(quoteIdentifier(aliases.get(node.id()))+"."+quoteIdentifier(column.effectiveStorageName())+" AS "+quoteIdentifier(result)); bindings.add(new SqlColumnBinding(result,entity.entityId(),field.id(),column.effectiveStorageName(),node.id(),true)); }
        else { result=existing.resultName(); int idx=bindings.indexOf(existing); bindings.set(idx,new SqlColumnBinding(existing.resultName(),existing.entityId(),existing.fieldId(),existing.columnName(),existing.nodeId(),true)); }
        cursorBindings.add(new SqlCursorBinding(result,entity.entityId(),field.id(),field.clrType(),direction));
    }
    private static List<SemanticOrderTerm> buildCursorOrder(EntityMetadata entity,List<SemanticOrderTerm> requested){
        List<SemanticOrderTerm> result=new ArrayList<>(requested); if(entity.primaryKey()==null) throw new IllegalStateException("Entity '"+entity.name()+"' has no primary-key metadata required for cursor pagination.");
        FieldMetadata pk=entity.effectiveFields().stream().filter(f->f.column()!=null&&f.column().equals(entity.primaryKey())).findFirst().orElseThrow(()->new IllegalStateException("Entity '"+entity.name()+"' primary key is not mapped to a semantic field."));
        if(result.stream().noneMatch(x->x.isRootField()&&x.aggregate()==SemanticOrderAggregate.NONE&&x.field().equals(pk.id()))) result.add(new SemanticOrderTerm(pk.id(),SemanticSortDirection.ASC)); return result;
    }
    private String buildSeekPredicate(List<ResolvedOrderTerm> order,List<JsonNode> values,List<SqlParameterBinding> parameters){
        if(order.isEmpty()) throw new IllegalStateException("Cursor pagination requires at least one ordering term."); List<String> branches=new ArrayList<>();
        for(int i=0;i<order.size();i++){ List<String> prefix=new ArrayList<>(); for(int j=0;j<i;j++){String ref=buildOrderReference(order.get(j));String p=addCursorParameter(values.get(j),order.get(j).field().clrType(),parameters);prefix.add(ref+" = @"+p);} String ref=buildOrderReference(order.get(i));String p=addCursorParameter(values.get(i),order.get(i).field().clrType(),parameters);prefix.add(ref+" "+(order.get(i).term().direction()==SemanticSortDirection.DESC?"<":">")+" @"+p); branches.add("("+String.join(" AND ",prefix)+")"); }
        return "("+String.join(" OR ",branches)+")";
    }
    private String buildOrderReference(ResolvedOrderTerm term){
        if(term.term().isAggregate()) return buildAggregateReference(term.term(),term.node(),term.entity(),term.field(),Map.of(term.node().id(),term.alias()));
        if(term.field().column()==null) throw new IllegalArgumentException("Order field '"+term.entity().name()+"."+term.field().name()+"' has no storage column mapping.");
        var c=term.entity().columns().stream().filter(x->x.id().equals(term.field().column().columnId())).findFirst().orElseThrow(); return quoteIdentifier(term.alias())+"."+quoteIdentifier(c.effectiveStorageName());
    }
    private String buildAggregateReference(SemanticOrderTerm term,ExecutionIRNode sourceNode,EntityMetadata sourceEntity,FieldMetadata field,Map<Integer,String> aliases){
        if(term.effectivePath().isEmpty()) throw new IllegalArgumentException("Aggregate ordering requires a relationship path."); if(term.effectivePath().size()!=1) throw new UnsupportedOperationException("Collection aggregation currently supports one relationship hop.");
        var relationship=metadata.getRelationship(term.effectivePath().get(0)); var target=metadata.getEntity(relationship.target()); String targetAlias="a"+sourceNode.id()+"_agg";
        var targetColumn=target.columns().stream().filter(c->c.id().equals(relationship.targetKey().columnId())).findFirst().orElseThrow(); var sourceColumn=sourceEntity.columns().stream().filter(c->c.id().equals(relationship.sourceKey().columnId())).findFirst().orElseThrow();
        String correlation=quoteIdentifier(targetAlias)+"."+quoteIdentifier(targetColumn.effectiveStorageName())+" = "+quoteIdentifier(aliases.get(sourceNode.id()))+"."+quoteIdentifier(sourceColumn.effectiveStorageName()); String aggregate;
        if(term.aggregate()==SemanticOrderAggregate.COUNT) aggregate="COUNT(*)"; else { if(field.column()==null) throw new IllegalArgumentException("Aggregate field '"+target.name()+"."+field.name()+"' has no storage column mapping."); var valueColumn=target.columns().stream().filter(c->c.id().equals(field.column().columnId())).findFirst().orElseThrow(); aggregate=(term.aggregate()==SemanticOrderAggregate.MIN?"MIN":"MAX")+"("+quoteIdentifier(targetAlias)+"."+quoteIdentifier(valueColumn.effectiveStorageName())+")"; }
        return "(SELECT "+aggregate+" FROM "+quoteStorageName(target.effectiveStorageName())+" "+quoteIdentifier(targetAlias)+" WHERE "+correlation+")";
    }
    private static String addCursorParameter(JsonNode value,Class<?> type,List<SqlParameterBinding> parameters){String name="p"+parameters.size();parameters.add(new SqlParameterBinding(name,CursorCodec.convertValue(value,type)));return name;}
    private String writeResolvedOrder(List<ResolvedOrderTerm> terms){if(terms.isEmpty())return "";List<String> parts=new ArrayList<>();for(var t:terms){String ref=buildOrderReference(t);parts.add(ref+(t.term().direction()==SemanticSortDirection.DESC?" DESC":" ASC"));}return "ORDER BY "+String.join(", ",parts);}
    private static ExecutionIRNode resolveOrderParentNode(ExecutionIRNode root,List<RelationshipId> path){ExecutionIRNode current=root;for(int i=0;i<path.size()-1;i++){RelationshipId r=path.get(i);current=current.children().stream().filter(x->r.equals(x.viaRelationship())).findFirst().orElseThrow(()->new IllegalArgumentException("Order path relationship '"+r+"' is not part of the execution IR."));}return current;}
    private static ExecutionIRNode resolveOrderNode(ExecutionIRNode root,List<RelationshipId> path){ExecutionIRNode current=root;for(RelationshipId r:path)current=current.children().stream().filter(x->r.equals(x.viaRelationship())).findFirst().orElseThrow(()->new IllegalArgumentException("Order path relationship '"+r+"' is not part of the execution IR. The relationship must be selected before it can be used for ordering."));return current;}
    private record ResolvedOrderTerm(SemanticOrderTerm term,ExecutionIRNode node,EntityMetadata entity,String alias,FieldMetadata field){}
    private record NodeOccurrence(ExecutionIRNode node,Integer parentId){}
    private static void collect(ExecutionIRNode node,List<NodeOccurrence> result,Integer parentId){result.add(new NodeOccurrence(node,parentId));for(var child:node.children())collect(child,result,node.id());}
    private String renderJoinColumn(ColumnReference reference,ExecutionIRNode parent,ExecutionIRNode child,Map<Integer,String> aliases){ExecutionIRNode node;if(parent.entityId().equals(reference.entityId()))node=parent;else if(child.entityId().equals(reference.entityId()))node=child;else throw new IllegalArgumentException("Join column entity '"+reference.entityId()+"' does not match the relationship endpoints.");var entity=metadata.getEntity(reference.entityId());var column=entity.columns().stream().filter(x->x.id().equals(reference.columnId())).findFirst().orElseThrow();return quoteIdentifier(aliases.get(node.id()))+"."+quoteIdentifier(column.effectiveStorageName());}
    public static String quoteIdentifier(String value){return "\""+value.replace("\"","\"\"")+"\"";}
    public static String quoteStorageName(String value){String[] parts=value.split("\\.");List<String> non=new ArrayList<>();for(String p:parts)if(!p.trim().isEmpty())non.add(p.trim());if(non.isEmpty())throw new IllegalArgumentException("Storage name cannot be empty.");return non.stream().map(SqlCompiler::quoteIdentifier).reduce((a,b)->a+"."+b).orElseThrow();}
}
