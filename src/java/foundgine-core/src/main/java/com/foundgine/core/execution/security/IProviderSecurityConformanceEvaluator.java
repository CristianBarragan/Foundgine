package com.foundgine.core.execution.security;

import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ProviderPlan;

/** Provider-specific certification hook inspecting the actual compiled plan. */
public interface IProviderSecurityConformanceEvaluator {
	ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan);
}
