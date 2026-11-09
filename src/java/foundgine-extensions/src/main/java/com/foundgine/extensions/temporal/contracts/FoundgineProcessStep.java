package com.foundgine.extensions.temporal.contracts;

import java.time.Duration;

/**
 * One step of a process: a named operation executed as a Temporal activity.
 *
 * @param name unique within the process; also the approval-gate and output key
 * @param operation name of a registered {@code IFoundgineTemporalOperation}
 * @param compensationOperation run (in reverse order) when a later step fails or the workflow is cancelled; may be null
 * @param requiresApproval pause before the step until an approval signal arrives
 * @param approvalTimeout optional maximum wait for approval (null waits indefinitely); on expiry the process is rejected
 */
public record FoundgineProcessStep(String name, String operation, String compensationOperation,
        boolean requiresApproval, Duration approvalTimeout) {
    public FoundgineProcessStep {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Step name is required.");
        if (operation == null || operation.isBlank()) throw new IllegalArgumentException("Step operation is required.");
    }

    public FoundgineProcessStep(String name, String operation) { this(name, operation, null, false, null); }

    public FoundgineProcessStep(String name, String operation, String compensationOperation) {
        this(name, operation, compensationOperation, false, null);
    }
}
