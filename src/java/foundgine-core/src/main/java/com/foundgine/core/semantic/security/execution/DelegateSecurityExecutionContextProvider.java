package com.foundgine.core.semantic.security.execution;

import java.util.Objects;
import java.util.function.Supplier;

/** Adapts a Java {@link Supplier} into the shared security-context provider. */
public final class DelegateSecurityExecutionContextProvider implements ISecurityExecutionContextProvider {

	private final Supplier<SecurityExecutionContext> factory;

	public DelegateSecurityExecutionContextProvider(Supplier<SecurityExecutionContext> factory) {
		this.factory = Objects.requireNonNull(factory, "factory");
	}

	@Override
	public SecurityExecutionContext getSecurityExecutionContext() {
		return factory.get();
	}
}
