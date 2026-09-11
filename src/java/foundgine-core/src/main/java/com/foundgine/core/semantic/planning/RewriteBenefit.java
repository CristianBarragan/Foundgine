package com.foundgine.core.semantic.planning;

/** Estimated execution benefit of applying a rewrite. */
public record RewriteBenefit(double estimatedBenefit) {
    public static RewriteBenefit from(double value){if(Double.isNaN(value)||Double.isInfinite(value)||value<0)throw new IllegalArgumentException("Rewrite benefit must be finite and non-negative.");return new RewriteBenefit(value);}
}
