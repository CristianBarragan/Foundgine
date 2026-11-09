package com.foundgine.extensions.temporal.workflows;

/** The workflow was cancelled (Temporal: CanceledFailureException). */
public final class FoundgineWorkflowCancelledException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    public FoundgineWorkflowCancelledException() { super("Workflow cancelled."); }
}
