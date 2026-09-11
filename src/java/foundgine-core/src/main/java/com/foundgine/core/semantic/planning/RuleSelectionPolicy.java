package com.foundgine.core.semantic.planning;

import java.util.Objects;

/** Policy controlling deterministic selection among simultaneously applicable rewrite rules. */
public record RuleSelectionPolicy(boolean preferHigherBenefit,boolean penalizeRewriteCost,double minimumScore) {
    public RuleSelectionPolicy(){this(true,true,Double.NEGATIVE_INFINITY);}
    public RuleSelectionPolicy validate(){if(Double.isNaN(minimumScore)||Double.isInfinite(minimumScore)&&minimumScore>0)throw new IllegalArgumentException("Minimum score is invalid.");return this;}
    public RewriteScore score(IPlanRewriteRule rule){Objects.requireNonNull(rule);var benefit=RewriteBenefit.from(Math.max(0d,rule.benefitEstimate()));var cost=RewriteCost.from(Math.max(0d,rule.costImpact()));if(!penalizeRewriteCost)return new RewriteScore(preferHigherBenefit?benefit.estimatedBenefit():-benefit.estimatedBenefit());var score=RewriteScore.calculate(benefit,cost).value();return new RewriteScore(preferHigherBenefit?score:-score);}
}
