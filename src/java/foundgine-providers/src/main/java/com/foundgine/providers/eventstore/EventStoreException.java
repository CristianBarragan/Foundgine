package com.foundgine.providers.eventstore;

/** Unchecked wrapper for database failures in the event store, so the neutral contract needs no JDBC types. */
public final class EventStoreException extends RuntimeException {
    public EventStoreException(String message, Throwable cause) { super(message, cause); }
}
