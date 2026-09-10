package com.foundgine.core.semantic.planning;

import java.util.*;

/** Auditable result of a composed rewrite sequence. */
public record RewriteRuleCompositionResult(SemanticPlan plan,List<PlanRewriteRuleResult> applications,double totalCostImpact,boolean terminatedNormally,List<RewriteRuleCandidate> selectionHistory,List<ProviderAwareRewriteRuleCandidate> providerSelectionHistory) {
    public RewriteRuleCompositionResult {applications=List.copyOf(applications);selectionHistory=selectionHistory==null?List.of():List.copyOf(selectionHistory);providerSelectionHistory=providerSelectionHistory==null?List.of():List.copyOf(providerSelectionHistory);}
    public RewriteRuleCompositionResult(SemanticPlan plan,List<PlanRewriteRuleResult> applications,double totalCostImpact,boolean terminatedNormally){this(plan,applications,totalCostImpact,terminatedNormally,List.of(),List.of());}
    public List<String> appliedRules(){return applications.stream().map(PlanRewriteRuleResult::ruleName).toList();}
    public boolean changed(){return !applications.isEmpty();}
}
