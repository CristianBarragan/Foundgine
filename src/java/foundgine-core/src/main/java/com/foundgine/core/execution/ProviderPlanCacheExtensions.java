package com.foundgine.core.execution;

import java.util.Objects;
import java.util.function.Supplier;

/**
 * C# exposes this as an extension method on {@code IProviderPlanCache}; Java
 * has no extension methods, so it is ported as a static helper taking the
 * cache as its first parameter.
 */
public final class ProviderPlanCacheExtensions {

    private ProviderPlanCacheExtensions() {
    }

    /**
     * Gets an existing provider plan or creates it once. The built-in memory
     * cache uses single-flight compilation so concurrent requests for the same
     * uncached key do not stampede the provider compiler.
     */
    public static ProviderPlan getOrAdd(IProviderPlanCache cache, String key, Supplier<ProviderPlan> factory) {
        Objects.requireNonNull(cache);
        Objects.requireNonNull(key);
        Objects.requireNonNull(factory);

        if (cache instanceof MemoryProviderPlanCache memory)
            return memory.getOrAdd(key, factory);

        ProviderPlan existing = cache.tryGet(key);
        if (existing != null)
            return existing;

        ProviderPlan created = factory.get();
        cache.set(key, created);
        return created;
    }
}
