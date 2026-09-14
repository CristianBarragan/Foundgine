package com.foundgine.providers.storage.inmemory;

import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ProviderPlan;

import java.util.Objects;

/**
 * Provider-specific plan for direct execution of Foundgine ExecutionIR over
 * CLR/Java objects.
 */
public final class InMemoryPlan extends ProviderPlan {
	private final ExecutionIR ir;

	public InMemoryPlan(ExecutionIR ir) {
		super("in-memory");
		this.ir = Objects.requireNonNull(ir, "ir");
	}

	public ExecutionIR ir() {
		return ir;
	}
}
