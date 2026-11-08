package com.foundgine.providers.outbox;

public final class PostgresOutboxOptions {
    private int maxPayloadBytes = 256 * 1024;
    private int maxHeaders = 64;

    public int maxPayloadBytes() { return maxPayloadBytes; }
    public int maxHeaders() { return maxHeaders; }
    public PostgresOutboxOptions maxPayloadBytes(int value) { maxPayloadBytes = value; return this; }
    public PostgresOutboxOptions maxHeaders(int value) { maxHeaders = value; return this; }

    public void validate() {
        if (maxPayloadBytes < 1) throw new IllegalArgumentException("maxPayloadBytes must be at least one");
        if (maxHeaders < 0) throw new IllegalArgumentException("maxHeaders must not be negative");
    }
}
