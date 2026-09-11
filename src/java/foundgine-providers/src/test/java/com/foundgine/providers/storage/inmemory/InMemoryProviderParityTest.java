package com.foundgine.providers.storage.inmemory;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class InMemoryProviderParityTest {
    @Test
    void providerCompilesToAnInMemoryPlanWithoutSqlSurface() {
        var provider = new InMemoryProvider(java.util.List.of());
        assertEquals("in-memory", provider.compile(null).provider());
    }

    @Test
    void providerPlanTypeIsProviderSpecificNotSql() {
        var provider = new InMemoryProvider(java.util.List.of());
        var plan = provider.compile(null);
        assertEquals("com.foundgine.providers.storage.inmemory.InMemoryProvider$Plan", plan.getClass().getName());
    }
}
