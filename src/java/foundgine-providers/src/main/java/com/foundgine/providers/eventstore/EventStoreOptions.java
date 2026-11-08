package com.foundgine.providers.eventstore;

public final class EventStoreOptions {
    private int maximumReplayBatchSize = 500;
    private int defaultReplayBatchSize = 100;

    public int maximumReplayBatchSize() { return maximumReplayBatchSize; }
    public int defaultReplayBatchSize() { return defaultReplayBatchSize; }
    public EventStoreOptions maximumReplayBatchSize(int value) { maximumReplayBatchSize = value; return this; }
    public EventStoreOptions defaultReplayBatchSize(int value) { defaultReplayBatchSize = value; return this; }

    public void validate() {
        if (maximumReplayBatchSize < 1 || maximumReplayBatchSize > 10_000)
            throw new IllegalArgumentException("maximumReplayBatchSize must be 1-10,000");
        if (defaultReplayBatchSize < 1 || defaultReplayBatchSize > maximumReplayBatchSize)
            throw new IllegalArgumentException("defaultReplayBatchSize must be 1-maximumReplayBatchSize");
    }
}
