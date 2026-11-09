package com.foundgine.extensions.temporal.activities;

/** Throw to fail the step without retries (validation failure, business rule, denial). */
public final class FoundgineNonRetryableOperationException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    public FoundgineNonRetryableOperationException(String message) { super(message); }

    public FoundgineNonRetryableOperationException(String message, Throwable cause) { super(message, cause); }
}
