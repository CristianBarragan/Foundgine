package com.foundgine.runtime.api;

import com.foundgine.core.semantic.planning.PlanInspection;

public record DryRunResult(PlanInspection inspection, boolean executionRequiredApproval) {
	public DryRunResult(PlanInspection inspection) {
		this(inspection, false);
	}
}
