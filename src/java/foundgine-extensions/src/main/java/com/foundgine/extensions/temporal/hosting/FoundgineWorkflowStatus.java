package com.foundgine.extensions.temporal.hosting;

/** Engine-level execution status (what Temporal reports), distinct from the process state the workflow tracks. */
public enum FoundgineWorkflowStatus {
    RUNNING("Running"), COMPLETED("Completed"), FAILED("Failed"), CANCELED("Canceled");

    private final String displayName;

    FoundgineWorkflowStatus(String displayName) { this.displayName = displayName; }

    public String displayName() { return displayName; }
}
