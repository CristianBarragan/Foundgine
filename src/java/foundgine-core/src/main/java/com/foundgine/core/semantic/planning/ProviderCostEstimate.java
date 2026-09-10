package com.foundgine.core.semantic.planning;

import java.util.Objects;

/** Provider-specific advisory estimate for executing a candidate semantic plan. */
public record ProviderCostEstimate(String provider,double estimatedExecutionCost,double estimatedRows,double confidence,CostEstimateProvenance provenance) {
    public ProviderCostEstimate(String provider,double estimatedExecutionCost){this(provider,estimatedExecutionCost,0d,.5d,null);}
    public ProviderCostEstimate { validateProvider(provider);validateNonNegative(estimatedExecutionCost,"estimatedExecutionCost");validateNonNegative(estimatedRows,"estimatedRows");if(Double.isNaN(confidence)||Double.isInfinite(confidence)||confidence<0||confidence>1)throw new IllegalArgumentException("Confidence must be between 0 and 1."); }
    public CostEstimateProvenance effectiveProvenance(){return provenance==null?CostEstimateProvenance.heuristic():provenance;}
    public static ProviderCostEstimate from(String provider,double cost){return new ProviderCostEstimate(provider,cost,0d,.5d,null);}
    public static ProviderCostEstimate from(String provider,double cost,double rows,double confidence,CostEstimateProvenance provenance){return new ProviderCostEstimate(provider,cost,rows,confidence,provenance);}
    private static void validateProvider(String provider){if(provider==null||provider.isBlank())throw new IllegalArgumentException("Provider is required.");}
    private static void validateNonNegative(double value,String name){if(Double.isNaN(value)||Double.isInfinite(value)||value<0)throw new IllegalArgumentException(name+" must be finite and non-negative.");}
}
