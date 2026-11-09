package com.foundgine.extensions.temporal.hosting;

import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.IFoundgineProvider;

/** Declares the workflow engine as the Foundgine Workflow provider so startup validation and discovery can see it. */
public final class FoundgineTemporalProvider implements IFoundgineProvider {
    @Override public String id() { return "foundgine.temporal.workflow"; }
    @Override public String name() { return "Temporal workflow engine"; }
    @Override public String version() { return "1.0.0"; }
    @Override public FoundgineProviderKind kind() { return FoundgineProviderKind.WORKFLOW; }

    @Override
    public long capabilities() {
        return FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_EXECUTION,
                FoundgineProviderCapability.WORKFLOW_SIGNALS, FoundgineProviderCapability.WORKFLOW_QUERIES,
                FoundgineProviderCapability.RETRY, FoundgineProviderCapability.TENANT_ISOLATION);
    }
}
