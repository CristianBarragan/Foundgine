package com.foundgine.core.execution;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.function.Supplier;

/** Small bounded process-local provider-plan cache. */
public final class MemoryProviderPlanCache implements IProviderPlanCache {
    private final int capacity;
    private final LinkedHashMap<String, ProviderPlan> plans =
            new LinkedHashMap<>(16, .75f, true);

    public MemoryProviderPlanCache() { this(256); }

    public MemoryProviderPlanCache(int capacity) {
        if (capacity <= 0) throw new IllegalArgumentException("capacity must be positive");
        this.capacity = capacity;
    }

    @Override
    public synchronized ProviderPlan tryGet(String key) {
        return plans.get(key);
    }

    /** Atomic get-or-create operation; failed factories do not poison the cache. */
    public synchronized ProviderPlan getOrAdd(String key, Supplier<ProviderPlan> factory) {
        Objects.requireNonNull(key, "key");
        Objects.requireNonNull(factory, "factory");
        ProviderPlan existing = plans.get(key);
        if (existing != null) return existing;
        ProviderPlan created = Objects.requireNonNull(factory.get(), "factory returned null");
        plans.put(key, created);
        trim();
        return created;
    }

    @Override
    public synchronized void set(String key, ProviderPlan plan) {
        Objects.requireNonNull(key, "key");
        Objects.requireNonNull(plan, "plan");
        plans.put(key, plan);
        trim();
    }

    public synchronized int size() { return plans.size(); }

    public synchronized void clear() { plans.clear(); }

    private void trim() {
        while (plans.size() > capacity) {
            Map.Entry<String, ProviderPlan> eldest = plans.entrySet().iterator().next();
            plans.remove(eldest.getKey());
        }
    }
}
