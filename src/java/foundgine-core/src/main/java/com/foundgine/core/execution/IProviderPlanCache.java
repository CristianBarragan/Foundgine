package com.foundgine.core.execution;

/** Cache for already-authorized provider plans. */
public interface IProviderPlanCache {
    ProviderPlan tryGet(String key);
    void set(String key, ProviderPlan plan);
}
