package com.foundgine.runtime;

import java.util.*;
import java.util.function.Supplier;

/**
 * Small framework-neutral service registry used by the Java runtime capability
 * surface. It deliberately avoids coupling Foundgine.Runtime to Spring/CDI;
 * applications can adapt these registrations into their preferred container.
 */
public final class FoundgineServiceRegistry {
	private final Map<Class<?>, Supplier<?>> suppliers = new LinkedHashMap<>();

	public <T> FoundgineServiceRegistry addSingleton(Class<T> type, Supplier<? extends T> supplier) {
		suppliers.put(Objects.requireNonNull(type), Objects.requireNonNull(supplier));
		return this;
	}

	public <T> FoundgineServiceRegistry addSingletonInstance(Class<T> type, T instance) {
		return addSingleton(type, () -> instance);
	}

	public <T> T getRequiredService(Class<T> type) {
		Supplier<?> supplier = suppliers.get(type);
		if (supplier == null)
			throw new IllegalStateException("Required service '" + type.getName() + "' is not registered.");
		Object value = supplier.get();
		if (!type.isInstance(value))
			throw new IllegalStateException("Registered service for '" + type.getName() + "' has incompatible type.");
		return type.cast(value);
	}

	public <T> Optional<T> getService(Class<T> type) {
		Supplier<?> supplier = suppliers.get(type);
		return supplier == null ? Optional.empty() : Optional.ofNullable(type.cast(supplier.get()));
	}

	public boolean contains(Class<?> type) {
		return suppliers.containsKey(type);
	}
}
