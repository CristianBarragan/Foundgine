package com.foundgine.core.execution;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class MemoryProviderPlanCacheTests {
	private static final class TestPlan extends ProviderPlan {
		TestPlan(String provider) {
			super(provider);
		}
	}

	@Test
	void cacheIsBoundedAndUsesLruOrder() {
		var cache = new MemoryProviderPlanCache(2);
		var a = new TestPlan("a");
		var b = new TestPlan("b");
		var c = new TestPlan("c");
		cache.set("a", a);
		cache.set("b", b);
		assertSame(a, cache.tryGet("a"));
		cache.set("c", c);
		assertNull(cache.tryGet("b"));
		assertSame(a, cache.tryGet("a"));
		assertSame(c, cache.tryGet("c"));
	}

	@Test
	void getOrAddDoesNotCacheFailedFactory() {
		var cache = new MemoryProviderPlanCache();
		assertThrows(RuntimeException.class, () -> cache.getOrAdd("bad", () -> {
			throw new RuntimeException("boom");
		}));
		var plan = new TestPlan("ok");
		assertSame(plan, cache.getOrAdd("x", () -> plan));
		assertSame(plan, cache.getOrAdd("x", () -> new TestPlan("wrong")));
	}
}
