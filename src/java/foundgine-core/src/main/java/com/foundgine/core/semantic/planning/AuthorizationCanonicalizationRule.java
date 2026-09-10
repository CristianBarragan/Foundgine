package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** Canonicalizes authorization boolean expressions without changing their provider-neutral meaning. */
public final class AuthorizationCanonicalizationRule implements IPlanRewriteRule {
    @Override public String name(){return "authorization.canonicalization";}
    @Override public List<String> preconditions(){return List.of("plan contains an authorization predicate","predicate uses supported boolean structure");}
    @Override public List<String> securityObligations(){return List.of("authorization.required","authorization.runtime");}
    @Override public double costImpact(){return 0d;}
    @Override public double benefitEstimate(){return 1d;}
    @Override public int priority(){return 0;}
    @Override public boolean canApply(SemanticPlan plan){Objects.requireNonNull(plan);return containsAuthorization(plan.root());}
    @Override public SemanticPlan apply(SemanticPlan plan){Objects.requireNonNull(plan);if(!canApply(plan))return plan;boolean[] changed={false};var root=rewriteNode(plan.root(),changed);return changed[0]?new SemanticPlan(root,plan.requiredSecurityInvariants(),plan.authorizationBinding()):plan;}
    private static SemanticPlanNode rewriteNode(SemanticPlanNode node,boolean[] changed){
        var authorization=node.authorization();var normalized=authorization==null?null:normalize(authorization,changed);
        List<SemanticPlanNode> children=new ArrayList<>(node.children().size());boolean childChanged=false;
        for(var child:node.children()){var r=rewriteNode(child,changed);children.add(r);childChanged|=r!=child;}
        boolean nodeChanged=normalized!=authorization||childChanged;if(nodeChanged)changed[0]=true;
        return nodeChanged?new SemanticPlanNode(node.id(),node.operation(),node.entityId(),node.fields(),node.viaRelationship(),node.viaConnection(),children,node.queryOptions(),normalized,node.relationshipCardinality(),node.traversalMode(),node.traversalOrder(),node.aggregateExecutionStrategy()):node;
    }
    private static boolean containsAuthorization(SemanticPlanNode node){return node.authorization()!=null||node.children().stream().anyMatch(AuthorizationCanonicalizationRule::containsAuthorization);}
    private static AuthorizationPredicate normalize(AuthorizationPredicate predicate,boolean[] changed){
        boolean localChanged=false;
        var left=predicate.left()==null?null:normalize(predicate.left(),new boolean[]{false});
        var right=predicate.right()==null?null:normalize(predicate.right(),new boolean[]{false});
        localChanged=left!=predicate.left()||right!=predicate.right();
        var current=left==predicate.left()&&right==predicate.right()?predicate:new AuthorizationPredicate(predicate.kind(),predicate.name(),predicate.value(),left,right);
        String before=structuralKey(predicate);
        if(current.kind()==AuthorizationPredicateKind.NOT&&current.left()!=null&&current.left().kind()==AuthorizationPredicateKind.NOT&&current.left().left()!=null){current=current.left().left();localChanged=true;}
        else if(current.kind()==AuthorizationPredicateKind.AND||current.kind()==AuthorizationPredicateKind.OR){
            var operands=new ArrayList<AuthorizationPredicate>();flatten(current.kind(),current,operands);
            var unique=new LinkedHashMap<String,AuthorizationPredicate>();for(var op:operands)unique.putIfAbsent(structuralKey(op),op);
            operands=new ArrayList<>(unique.values());operands.sort(Comparator.comparing(AuthorizationCanonicalizationRule::structuralKey));
            current=operands.size()==1?operands.get(0):rebuildBalanced(current.kind(),operands);localChanged=true;
        }
        if(!before.equals(structuralKey(current))){changed[0]=true;return current;}
        return localChanged?current:predicate;
    }
    private static void flatten(AuthorizationPredicateKind kind,AuthorizationPredicate node,List<AuthorizationPredicate> out){if(node.kind()==kind){if(node.left()!=null)flatten(kind,node.left(),out);if(node.right()!=null)flatten(kind,node.right(),out);}else out.add(node);}
    private static AuthorizationPredicate rebuildBalanced(AuthorizationPredicateKind kind,List<AuthorizationPredicate> operands){AuthorizationPredicate result=operands.get(0);for(int i=1;i<operands.size();i++)result=kind==AuthorizationPredicateKind.AND?AuthorizationPredicate.and(result,operands.get(i)):AuthorizationPredicate.or(result,operands.get(i));return result;}
    private static String structuralKey(AuthorizationPredicate p){StringBuilder b=new StringBuilder(96);appendKey(b,p);return b.toString();}
    private static void appendKey(StringBuilder b,AuthorizationPredicate p){b.append((byte)p.kind().ordinal()).append('(');appendValue(b,p.name());b.append('|');appendValue(b,p.value());b.append('|');if(p.left()!=null)appendKey(b,p.left());b.append('|');if(p.right()!=null)appendKey(b,p.right());b.append(')');}
    private static void appendValue(StringBuilder b,String value){if(value==null){b.append("null");return;}b.append(value.length()).append(':').append(value);}
}
