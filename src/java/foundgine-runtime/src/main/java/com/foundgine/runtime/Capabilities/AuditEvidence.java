package com.foundgine.runtime.capabilities;

import com.foundgine.runtime.*;

/**
 * Enables the runtime tool-governance/audit capability in the service registry.
 */
public final class AuditEvidence implements IFoundgineCapability {
	@Override
	public void configure(FoundgineCapabilityContext context) {
		// Governance is registered explicitly through ToolGovernanceBuilder in Java.
		context.services().addSingletonInstance(AuditEvidence.class, this);
	}
}
