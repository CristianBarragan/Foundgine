package com.foundgine.core.semantic.planning;
public interface IProviderCostEstimator { String provider(); ProviderCostEstimate estimate(SemanticPlan before,SemanticPlan candidate,IPlanRewriteRule rule); }
