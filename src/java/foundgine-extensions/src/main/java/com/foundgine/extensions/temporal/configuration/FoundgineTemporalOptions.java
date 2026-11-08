package com.foundgine.extensions.temporal.configuration;

import com.foundgine.extensions.temporal.contracts.FoundgineActivityPolicy;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Map;

/** Connection and execution defaults for the optional Temporal integration. Port of {@code FoundgineTemporalOptions}. */
public final class FoundgineTemporalOptions {
    public String address = "localhost:7233";
    public String namespace = "default";
    public String taskQueue = "foundgine";
    /** Prefix of every workflow id. Ids are {@code {prefix}/{tenant}/{process}/{businessKey}}. */
    public String workflowIdPrefix = "foundgine";
    /** Enables TLS (required by Temporal Cloud and any production cluster). */
    public boolean useTls;
    /** Temporal Cloud API key. Read it from a secret store; never commit it. */
    public String apiKey;

    public Duration activityStartToCloseTimeout = Duration.ofMinutes(2);
    public int activityMaximumAttempts = 5;
    public Duration activityInitialRetryInterval = Duration.ofSeconds(2);
    public Duration activityMaximumRetryInterval = Duration.ofMinutes(1);

    /** Outbox topic to process name. Messages on unmapped topics follow {@link #unroutedTopicBehavior}. */
    public final Map<String, String> topicRoutes = new LinkedHashMap<>();
    public UnroutedTopicBehavior unroutedTopicBehavior = UnroutedTopicBehavior.IGNORE;

    public FoundgineActivityPolicy toActivityPolicy() {
        return new FoundgineActivityPolicy(activityStartToCloseTimeout, activityMaximumAttempts,
                activityInitialRetryInterval, activityMaximumRetryInterval);
    }

    public void validate() {
        if (blank(address)) throw new IllegalArgumentException("Address is required.");
        if (blank(namespace)) throw new IllegalArgumentException("Namespace is required.");
        if (blank(taskQueue)) throw new IllegalArgumentException("TaskQueue is required.");
        if (blank(workflowIdPrefix) || workflowIdPrefix.contains("/"))
            throw new IllegalArgumentException("WorkflowIdPrefix must be non-empty and contain no '/'.");
        if (activityStartToCloseTimeout.isZero() || activityStartToCloseTimeout.isNegative())
            throw new IllegalArgumentException("ActivityStartToCloseTimeout must be positive.");
        if (activityMaximumAttempts < 1) throw new IllegalArgumentException("ActivityMaximumAttempts must be at least 1.");
        if (activityInitialRetryInterval.isZero() || activityInitialRetryInterval.isNegative()
                || activityMaximumRetryInterval.compareTo(activityInitialRetryInterval) < 0)
            throw new IllegalArgumentException("Retry intervals must be positive and maximum >= initial.");
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
