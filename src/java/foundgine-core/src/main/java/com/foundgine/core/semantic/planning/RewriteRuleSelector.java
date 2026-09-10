package com.foundgine.core.semantic.planning;

import java.util.*;

/** Selects the best currently applicable rewrite candidate deterministically. */
public final class RewriteRuleSelector {
    private final RuleSelectionPolicy policy;
    private final IProviderCostEstimator providerCostEstimator;
    private final ProviderCostSelectionPolicy providerPolicy;
    public RewriteRuleSelector(){this(null,null,null);}
    public RewriteRuleSelector(RuleSelectionPolicy policy,IProviderCostEstimator estimator,ProviderCostSelectionPolicy providerPolicy){this.policy=(policy==null?new RuleSelectionPolicy():policy).validate();this.providerCostEstimator=estimator;this.providerPolicy=(providerPolicy==null?new ProviderCostSelectionPolicy():providerPolicy).validate();}
    public RewriteRuleCandidate select(SemanticPlan plan,Iterable<IPlanRewriteRule> candidates){Objects.requireNonNull(plan);Objects.requireNonNull(candidates);var list=new ArrayList<RewriteRuleCandidate>();for(var rule:candidates)if(rule.canApply(plan)){var c=new RewriteRuleCandidate(rule.name(),rule.benefitEstimate(),rule.costImpact(),policy.score(rule).value(),rule.priority());if(c.score()>=policy.minimumScore())list.add(c);}list.sort(Comparator.comparingDouble(RewriteRuleCandidate::score).reversed().thenComparing(Comparator.comparingInt(RewriteRuleCandidate::priority).reversed()).thenComparing(RewriteRuleCandidate::ruleName));return list.isEmpty()?null:list.get(0);}
    public ProviderAwareRewriteRuleCandidate selectProviderAware(SemanticPlan plan,Iterable<IPlanRewriteRule> candidates){Objects.requireNonNull(plan);Objects.requireNonNull(candidates);if(providerCostEstimator==null)throw new IllegalStateException("A provider cost estimator is required for provider-aware selection.");var list=new ArrayList<ProviderAwareRewriteRuleCandidate>();for(var rule:candidates)if(rule.canApply(plan)){var candidatePlan=rule.apply(plan);var estimate=providerCostEstimator.estimate(plan,candidatePlan,rule);var benefit=RewriteBenefit.from(Math.max(0,rule.benefitEstimate()));var cost=RewriteCost.from(Math.max(0,rule.costImpact()));var score=ProviderAwareRewriteScore.calculate(benefit,cost,estimate,providerPolicy).value();var c=new ProviderAwareRewriteRuleCandidate(rule.name(),estimate.provider(),benefit.estimatedBenefit(),cost.estimatedWork(),estimate.estimatedExecutionCost(),estimate.estimatedRows(),estimate.confidence(),score,rule.priority());if(c.score()>=policy.minimumScore())list.add(c);}list.sort(Comparator.comparingDouble(ProviderAwareRewriteRuleCandidate::score).reversed().thenComparing(Comparator.comparingInt(ProviderAwareRewriteRuleCandidate::priority).reversed()).thenComparing(ProviderAwareRewriteRuleCandidate::ruleName));return list.isEmpty()?null:list.get(0);}
}
