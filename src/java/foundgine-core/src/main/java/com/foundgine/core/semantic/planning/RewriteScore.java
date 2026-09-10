package com.foundgine.core.semantic.planning;

/** Deterministic score used only to rank currently applicable rules. */
public record RewriteScore(double value) {
    public static RewriteScore calculate(RewriteBenefit benefit,RewriteCost cost){return new RewriteScore(benefit.estimatedBenefit()/(1d+cost.estimatedWork()));}
}
