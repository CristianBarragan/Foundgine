package com.foundgine.extensions.temporal.activities;

/** What the engine tells an activity about itself (Temporal: {@code ActivityExecutionContext.Info}). */
public record FoundgineActivityInfo(String workflowId, String activityId, int attempt) { }
