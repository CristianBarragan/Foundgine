package com.foundgine.core.execution;

/**
 * Compiles provider-neutral execution work into a provider-specific plan.
 * Providers consume ExecutionIR only; semantic planning artifacts never cross
 * this boundary.
 */
public interface IProviderPlanCompiler {
    ProviderPlan compile(ExecutionIR ir);
}
