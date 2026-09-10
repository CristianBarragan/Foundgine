package com.foundgine.core.semantic.planning;

import java.util.*;

/** Auditable result of applying one rewrite rule. */
public record PlanRewriteRuleResult(String ruleName,SemanticPlan before,SemanticPlan after,List<String> preconditions,List<String> securityObligations,double costImpact,SecurityPreservationProof securityProof,SecurityObligationProof securityObligationProof,SemanticEquivalenceProof semanticProof,SemanticPlanAuthorizationBindingProof authorizationBindingProof,PlanOptimizationProof optimizationProof) {
    public PlanRewriteRuleResult {preconditions=List.copyOf(preconditions);securityObligations=List.copyOf(securityObligations);}
    public boolean isSatisfied(){return securityProof.isSatisfied()&&securityObligationProof.isSatisfied()&&semanticProof.isSatisfied()&&authorizationBindingProof.isSatisfied()&&(optimizationProof==null||optimizationProof.isSatisfied());}
}
