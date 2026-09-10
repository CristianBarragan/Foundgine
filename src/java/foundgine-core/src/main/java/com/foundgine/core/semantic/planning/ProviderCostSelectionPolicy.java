package com.foundgine.core.semantic.planning;

/** Controls how provider execution cost participates in rewrite ranking. */
public record ProviderCostSelectionPolicy(boolean preferLowerProviderCost,double providerCostWeight) {
    public ProviderCostSelectionPolicy(){this(true,1d);}
    public ProviderCostSelectionPolicy validate(){if(Double.isNaN(providerCostWeight)||Double.isInfinite(providerCostWeight)||providerCostWeight<0)throw new IllegalArgumentException("Provider cost weight must be finite and non-negative.");return this;}
}
