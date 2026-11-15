using System.Collections.Concurrent;
using Foundgine.Providers.Redis;
using Xunit;

namespace Foundgine.Redis.Tests;

/// <summary>Exercises the store's logic against a fake backend with SET NX PX semantics; no server is needed.</summary>
public sealed class RedisSecurityWarrantReplayStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeBackend : IWarrantReplayBackend
    {
        public ConcurrentDictionary<string, TimeSpan> Keys { get; } = new(StringComparer.Ordinal);
        public ConcurrentQueue<TimeSpan> Ttls { get; } = new();

        public bool SetIfAbsent(string key, TimeSpan ttl)
        {
            Ttls.Enqueue(ttl);
            return Keys.TryAdd(key, ttl);
        }
    }

    private sealed class DownBackend : IWarrantReplayBackend
    {
        public bool SetIfAbsent(string key, TimeSpan ttl) => throw new IOException("connection refused");
    }

    private static RedisSecurityWarrantReplayStore Store(IWarrantReplayBackend backend) =>
        new(backend, timeProvider: new FixedTimeProvider());

    [Fact]
    public void First_consume_wins_and_every_second_is_rejected()
    {
        var store = Store(new FakeBackend());

        Assert.True(store.TryConsume("w", "n", Now.AddSeconds(300)));
        Assert.False(store.TryConsume("w", "n", Now.AddSeconds(300)));
        Assert.True(store.TryConsume("w", "other-nonce", Now.AddSeconds(300)));
        Assert.True(store.TryConsume("other-warrant", "n", Now.AddSeconds(300)));
    }

    [Fact]
    public void Ttl_is_remaining_lifetime_plus_skew_grace()
    {
        var backend = new FakeBackend();
        Store(backend).TryConsume("w", "n", Now.AddSeconds(300));

        Assert.Equal(TimeSpan.FromSeconds(360), Assert.Single(backend.Ttls));
    }

    [Fact]
    public void Already_expired_warrant_still_gets_the_short_grace_ttl_never_zero_or_negative()
    {
        var backend = new FakeBackend();
        Store(backend).TryConsume("w", "n", Now.AddSeconds(-30));

        Assert.Equal(TimeSpan.FromMinutes(1), Assert.Single(backend.Ttls));
    }

    [Fact]
    public void Two_argument_overload_uses_the_default_retention()
    {
        var backend = new FakeBackend();

        Assert.True(Store(backend).TryConsume("w", "n"));
        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(backend.Ttls));
    }

    [Fact]
    public void Absurd_expiry_is_capped_so_the_server_does_not_reject_it()
    {
        var backend = new FakeBackend();
        Store(backend).TryConsume("w", "n", DateTimeOffset.MaxValue);

        Assert.Equal(TimeSpan.FromDays(3650), Assert.Single(backend.Ttls));
    }

    [Fact]
    public void Keys_are_hashed_with_the_prefix_and_never_contain_the_raw_nonce()
    {
        var backend = new FakeBackend();
        Store(backend).TryConsume("warrant-id", "secret-nonce-value", Now.AddSeconds(300));

        var key = Assert.Single(backend.Keys.Keys);
        Assert.StartsWith(RedisSecurityWarrantReplayStore.DefaultKeyPrefix, key);
        Assert.DoesNotContain("secret-nonce-value", key);
        Assert.DoesNotContain("warrant-id", key);
        Assert.Matches("^[0-9A-F]{64}$", key[RedisSecurityWarrantReplayStore.DefaultKeyPrefix.Length..]);
    }

    [Fact]
    public void Same_identity_as_the_file_store_and_the_java_store_so_keys_are_stable_across_languages()
    {
        var backend = new FakeBackend();
        Store(backend).TryConsume("w-1", "n-1", Now.AddSeconds(300));

        Assert.Equal(
            "foundgine:warrant-replay:4F315D677F0C686D00E3CABD5847218BA0F8413BE77708988FB7197AE5618C7E",
            Assert.Single(backend.Keys.Keys));
    }

    [Fact]
    public void Backend_failure_fails_closed_and_never_returns_true()
    {
        var store = Store(new DownBackend());

        var ex = Assert.Throws<InvalidOperationException>(() => store.TryConsume("w", "n", Now.AddSeconds(300)));
        Assert.Contains("failing closed", ex.Message);
        Assert.IsType<IOException>(ex.InnerException);
        Assert.Throws<InvalidOperationException>(() => store.TryConsume("w", "n"));
    }

    [Fact]
    public void Blank_identity_is_rejected_before_touching_the_backend()
    {
        var backend = new FakeBackend();
        var store = Store(backend);

        Assert.Throws<ArgumentException>(() => store.TryConsume(" ", "n", Now));
        Assert.Throws<ArgumentNullException>(() => store.TryConsume("w", null!, Now));
        Assert.Empty(backend.Ttls);
    }

    [Fact]
    public void Exactly_one_of_many_concurrent_consumers_wins()
    {
        var store = Store(new FakeBackend());
        var winners = 0;

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 64 }, _ =>
        {
            if (store.TryConsume("race", "n", Now.AddSeconds(300)))
                Interlocked.Increment(ref winners);
        });

        Assert.Equal(1, winners);
    }

    [Fact]
    public void Non_positive_options_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RedisSecurityWarrantReplayStore(new FakeBackend(), grace: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RedisSecurityWarrantReplayStore(new FakeBackend(), defaultRetention: TimeSpan.FromSeconds(-1)));
    }
}
