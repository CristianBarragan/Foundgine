package com.foundgine.extensions.temporal.hosting;

import com.foundgine.extensions.temporal.contracts.FoundgineApprovalDecision;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessInput;

/**
 * The workflow-engine seam, shaped after the parts of {@code ITemporalClient} the integration uses. Adapt the
 * Temporal Java SDK to it in the host; {@link InProcessFoundgineWorkflowEngine} is a reference implementation for
 * samples and tests.
 */
public interface IFoundgineWorkflowClient {
    /**
     * Starts the process under a deterministic id. A running duplicate is attached to and a successfully finished
     * duplicate is rejected (Temporal: {@code UseExisting} + {@code AllowDuplicateFailedOnly}); a failed or cancelled
     * one may be started again.
     *
     * @return true when a new execution was started, false when the id already existed
     */
    boolean start(String workflowId, FoundgineProcessInput input);

    /** @return null when no such workflow exists */
    FoundgineWorkflowDescription describe(String workflowId);

    /** Delivers the {@code decision} signal. @return false when no such workflow exists */
    boolean signalDecision(String workflowId, FoundgineApprovalDecision decision);

    /** Requests cancellation. @return false when no such workflow exists */
    boolean cancel(String workflowId);
}
