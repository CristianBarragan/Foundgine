
using Foundgine.Providers.EventStore;
using Npgsql;
using Xunit;

namespace Foundgine.EventStore.Tests;

public sealed class EventStoreOptionsTests
{
    [Fact]
    public void Rejects_default_batch_larger_than_maximum()
    {
        var options = new EventStoreOptions
        {
            MaximumReplayBatchSize = 10,
            DefaultReplayBatchSize = 11
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgresEventStore(CreateTestDataSource(), options));
    }

    [Fact]
    public void Rejects_zero_maximum_batch()
    {
        var options = new EventStoreOptions { MaximumReplayBatchSize = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgresEventStore(CreateTestDataSource(), options));
    }
    
    private static NpgsqlDataSource CreateTestDataSource()
    {
        return NpgsqlDataSource.Create(
            "Host=localhost;Database=foundgine;Username=foundgine;Password=foundgine");
    }
}
