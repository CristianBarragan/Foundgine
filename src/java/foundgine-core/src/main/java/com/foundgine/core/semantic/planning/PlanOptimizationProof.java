package com.foundgine.core.semantic.planning;

import java.util.Objects;

/** Admissibility proof for a semantic-plan optimization. */
public record PlanOptimizationProof(String ruleName,boolean semanticMeaningPreserved,boolean securityPreserved,boolean authorizationBindingPreserved,double estimatedBenefit,double estimatedRewriteCost) {
    public boolean isSatisfied(){return semanticMeaningPreserved&&securityPreserved&&authorizationBindingPreserved&&estimatedBenefit>=0&&estimatedRewriteCost>=0;}
    public static PlanOptimizationProof create(IPlanRewriteRule rule,SemanticPlan before,SemanticPlan after){Objects.requireNonNull(rule);var semantic=SemanticEquivalenceProof.create(before,after);var security=SecurityPreservationProof.create(before,after);var authorization=SemanticPlanAuthorizationBindingProof.create(before,after);var proof=new PlanOptimizationProof(rule.name(),semantic.isSatisfied(),security.isSatisfied(),authorization.isSatisfied(),Math.max(0,rule.benefitEstimate()),Math.max(0,rule.costImpact()));if(!proof.isSatisfied())throw new IllegalStateException("Optimization '"+rule.name()+"' is not admissible because one or more preservation obligations failed.");return proof;}
}
