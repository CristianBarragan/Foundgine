package com.foundgine.extensions.temporal.hosting;

public interface IFoundgineWorkflowStarter {
    FoundgineWorkflowStartResult start(FoundgineWorkflowStartRequest request);

    /** Deterministic id for tenant/process/business key; throws {@link IllegalArgumentException} on unsafe segments. */
    String workflowId(String tenantId, String process, String businessKey);
}
