package com.foundgine.providers.outbox;

import java.util.UUID;

/** A time-limited claim on a message. complete/fail only succeed for the current token holder. */
public record FoundgineOutboxLease(FoundgineOutboxMessage message, int attempt, UUID leaseToken) { }
