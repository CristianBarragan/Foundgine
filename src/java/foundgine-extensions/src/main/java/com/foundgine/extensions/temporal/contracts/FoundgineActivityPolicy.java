package com.foundgine.extensions.temporal.contracts;

import java.time.Duration;

/** Activity retry/timeout policy captured at start so workflow code stays deterministic across deploys. */
public record FoundgineActivityPolicy(Duration startToCloseTimeout, int maximumAttempts, Duration initialInterval,
        Duration maximumInterval) { }
