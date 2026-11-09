package com.foundgine.extensions.temporal.activities;

/** An activity failed for good (or may be retried when {@link #nonRetryable()} is false). Mirrors Temporal's ApplicationFailure. */
public final class FoundgineActivityFailureException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    private final String errorType;
    private final boolean nonRetryable;

    public FoundgineActivityFailureException(String message, Throwable cause, String errorType, boolean nonRetryable) {
        super(message, cause);
        this.errorType = errorType;
        this.nonRetryable = nonRetryable;
    }

    public String errorType() { return errorType; }

    public boolean nonRetryable() { return nonRetryable; }
}
