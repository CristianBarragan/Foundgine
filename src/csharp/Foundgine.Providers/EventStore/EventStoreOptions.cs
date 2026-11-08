namespace Foundgine.Providers.EventStore;

public sealed class EventStoreOptions
{
    public int MaximumReplayBatchSize { get; init; } = 500;
    public int DefaultReplayBatchSize { get; init; } = 100;

    internal void Validate()
    {
        if (MaximumReplayBatchSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumReplayBatchSize));
        if (DefaultReplayBatchSize < 1 || DefaultReplayBatchSize > MaximumReplayBatchSize)
            throw new ArgumentOutOfRangeException(nameof(DefaultReplayBatchSize));
    }
}
