package com.foundgine.extensions.temporal.configuration;

public enum UnroutedTopicBehavior {
    /** Acknowledge and drop. Use when the outbox also feeds other consumers. */
    IGNORE,
    /** Throw so the outbox retries and eventually dead-letters the message. */
    FAIL
}
