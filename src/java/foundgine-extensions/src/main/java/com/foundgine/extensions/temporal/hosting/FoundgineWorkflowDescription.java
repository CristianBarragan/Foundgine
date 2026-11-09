package com.foundgine.extensions.temporal.hosting;

import com.foundgine.extensions.temporal.contracts.FoundgineProcessStatus;

/** @param process the queried process status; only populated while the workflow is running */
public record FoundgineWorkflowDescription(String workflowId, FoundgineWorkflowStatus status,
        FoundgineProcessStatus process) { }
