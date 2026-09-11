package com.foundgine.providers.storage.sql.security;

import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.security.*;
import com.foundgine.providers.storage.sql.*;
import java.util.*;

/** Structural conformance checks for compiled SQL plans. */
public final class SqlSecurityConformance {
    private SqlSecurityConformance() {}
    public record SqlSecurityConformanceResult(List<String> required,List<String> satisfied,List<String> violations){
        public boolean isSatisfied(){return violations.isEmpty();}
        public void ensureSatisfied(){if(!isSatisfied())throw new IllegalStateException("SQL security conformance failed: "+String.join("; ",violations));}
    }
    public static SqlSecurityConformanceResult verify(ExecutionIR ir,SqlPlan plan){
        Objects.requireNonNull(ir);Objects.requireNonNull(plan);List<String> required=ir.requiredSecurityInvariants().stream().distinct().sorted().toList();List<String> satisfied=new ArrayList<>(), violations=new ArrayList<>();
        for(String invariant:required){if(!SecurityInvariantRegistry.contains(invariant)){violations.add("Unknown security invariant '"+invariant+"'.");continue;}switch(invariant){
            case SecurityInvariantIds.AUTHORIZATION_REQUIRED,SecurityInvariantIds.RUNTIME_AUTHORIZATION -> verifyAuthorization(invariant,plan,satisfied,violations);
            case SecurityInvariantIds.PARAMETERIZED_VALUES -> verifyParameterized(plan,satisfied,violations);
            case SecurityInvariantIds.FIELD_VISIBILITY -> verifyFields(plan,satisfied,violations);
            case SecurityInvariantIds.RELATIONSHIP_VISIBILITY -> verifyRelationships(ir,plan,satisfied,violations);
            case SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION -> verifyContext(plan,satisfied,violations);
            default -> violations.add("Invariant '"+invariant+"' requires a provider-specific conformance check and cannot be inferred from SqlPlan alone.");
        }}return new SqlSecurityConformanceResult(required,satisfied,violations);
    }
    public static void ensureSatisfied(ExecutionIR ir,SqlPlan plan){verify(ir,plan).ensureSatisfied();}
    private static void verifyAuthorization(String invariant,SqlPlan plan,List<String>s,List<String>v){if(plan.authorization()==null||plan.authorization().isEmpty()){v.add(invariant+" requires at least one compiled authorization predicate.");return;}long distinct=plan.authorization().stream().map(SqlAuthorizationPredicate::nodeId).distinct().count();if(distinct!=plan.authorization().size()){v.add(invariant+" produced duplicate authorization predicates for the same execution node.");return;}if(invariant.equals(SecurityInvariantIds.RUNTIME_AUTHORIZATION)){var ps=plan.effectiveParameters().stream().filter(x->x.name().startsWith("auth")).toList();if(ps.isEmpty()||ps.stream().anyMatch(x->(x.contextPath()==null||x.contextPath().isBlank())&&x.value()==null)){v.add("authorization.runtime requires authorization context values to remain bound parameters.");return;}}s.add(invariant);}
    private static void verifyParameterized(SqlPlan p,List<String>s,List<String>v){if(p.effectiveParameters()==null){v.add("execution.parameterized-values requires a parameter binding collection.");return;}if(p.effectiveParameters().stream().anyMatch(x->x.name()==null||x.name().isBlank())){v.add("execution.parameterized-values contains an unnamed parameter binding.");return;}s.add(SecurityInvariantIds.PARAMETERIZED_VALUES);}
    private static void verifyFields(SqlPlan p,List<String>s,List<String>v){if(p.columns().isEmpty()){v.add("visibility.field requires an explicit SQL column projection.");return;}if(p.columns().stream().anyMatch(x->x.columnName()==null||x.columnName().isBlank())){v.add("visibility.field contains an SQL projection without a mapped storage column.");return;}s.add(SecurityInvariantIds.FIELD_VISIBILITY);}
    private static void verifyRelationships(ExecutionIR ir,SqlPlan p,List<String>s,List<String>v){List<ExecutionIRNode> nodes=flatten(ir.root());Set<Integer> projected=new HashSet<>();p.columns().forEach(x->projected.add(x.nodeId()));if(nodes.stream().anyMatch(x->!x.children().isEmpty())&&projected.isEmpty()){v.add("visibility.relationship requires an explicit projected execution shape.");return;}s.add(SecurityInvariantIds.RELATIONSHIP_VISIBILITY);}
    private static void verifyContext(SqlPlan p,List<String>s,List<String>v){for(var b:p.effectiveParameters())if(b.contextPath()!=null&&p.commandText().contains(String.valueOf(b.value()==null?"\0":b.value()))){v.add("planning.cache-context-isolation found a context value embedded in CommandText.");return;}s.add(SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION);}
    private static List<ExecutionIRNode> flatten(ExecutionIRNode n){List<ExecutionIRNode> r=new ArrayList<>();r.add(n);for(var c:n.children())r.addAll(flatten(c));return r;}
}
