package com.foundgine.extensions.temporal.workflows;

/** The process failed after compensation ran; the cause is the activity failure. */
public final class FoundgineProcessFailedException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    public FoundgineProcessFailedException(String message, Throwable cause) { super(message, cause); }
}
