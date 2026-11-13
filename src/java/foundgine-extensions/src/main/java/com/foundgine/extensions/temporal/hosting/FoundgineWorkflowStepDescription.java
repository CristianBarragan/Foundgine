package com.foundgine.extensions.temporal.hosting;

/** Public step metadata. Internal activity operation identifiers are deliberately omitted. */
public record FoundgineWorkflowStepDescription(String name, boolean requiresApproval,
        boolean hasCompensation, Double approvalTimeoutSeconds) { }
