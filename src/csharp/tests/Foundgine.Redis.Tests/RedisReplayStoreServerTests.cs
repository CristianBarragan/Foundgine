using Foundgine.Providers.Redis;
using Xunit;
using Xunit.Sdk;

namespace Foundgine.Redis.Tests;

/// <summary>Opt-in live-server tests for atomic replay protection across independent store instances.</summary>
public sealed class RedisReplayStoreServerTests
{
    [RedisFact]
    public void First_consume_wins_across_store_instances_sharing_a_server()
    {
        var redis = RespRedis.FromEnvironment() ?? throw SkipException.ForSkip("Set FOUNDGINE_REDIS to run live Redis integration tests.");
        var prefix = $"foundgine:test:{Guid.NewGuid():N}:";
        var first = new RedisSecurityWarrantReplayStore(redis, prefix);
        var second = new RedisSecurityWarrantReplayStore(redis, prefix);
        Assert.True(first.TryConsume("warrant", "nonce"));
        Assert.False(second.TryConsume("warrant", "nonce"));
    }

    [RedisFact]
    public void Ttl_is_set_on_the_server_and_the_entry_disappears_after_it()
    {
        var redis = RespRedis.FromEnvironment() ?? throw SkipException.ForSkip("Set FOUNDGINE_REDIS to run live Redis integration tests.");
        var prefix = $"foundgine:test:{Guid.NewGuid():N}:";
        var store = new RedisSecurityWarrantReplayStore(redis, prefix);
        Assert.True(store.TryConsume("w", "n", DateTimeOffset.UtcNow.AddSeconds(3)));
        // Locate the unique key in the isolated test prefix via a short-lived server-side scan.
        var scan = redis.ScanKeys(prefix + "*");
        Assert.Single(scan);
        Assert.InRange(redis.Pttl(scan[0]), 60_000, 63_000);
    }

    [RedisFact]
    public void Exactly_one_of_many_concurrent_consumers_wins_on_a_real_server()
    {
        var redis = RespRedis.FromEnvironment() ?? throw SkipException.ForSkip("Set FOUNDGINE_REDIS to run live Redis integration tests.");
        var prefix = $"foundgine:test:{Guid.NewGuid():N}:";
        var stores = Enumerable.Range(0, 8).Select(_ => new RedisSecurityWarrantReplayStore(redis, prefix)).ToArray();
        var winners = 0;
        Parallel.For(0, 64, i => { if (stores[i % stores.Length].TryConsume("parallel", "nonce")) Interlocked.Increment(ref winners); });
        Assert.Equal(1, winners);
    }
}
