using Foundgine.Providers.Redis;
using Foundgine.Providers.Tools.MCP;
using Xunit;
using Xunit.Sdk;

namespace Foundgine.Redis.Tests;

/// <summary>Opt-in integration tests. Set FOUNDGINE_REDIS to host:port or redis://[:password@]host:port/db.</summary>
public sealed class RedisFoundgineMcpSessionStoreTests
{
    private static RespRedis Server() => RespRedis.FromEnvironment() ?? throw SkipException.ForSkip("Set FOUNDGINE_REDIS to run live Redis integration tests.");
    private static RedisFoundgineMcpSessionStore Store(RespRedis server, string prefix) => new(server, prefix);
    private static string Prefix() => $"{{fgtest:{Guid.NewGuid():N}}}:";
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [RedisFact]
    public void Add_find_round_trip_and_collision()
    {
        var redis = Server();
        var store = Store(redis, Prefix());
        var now = Now();
        var item = new FoundgineMcpStoredSession("digest", "owner", now, now + 10, now + 100_000, now + 200_000);
        Assert.True(store.TryAdd(item));
        Assert.False(store.TryAdd(item));
        Assert.Equal(item, store.Find("digest"));
    }

    [RedisFact]
    public void Touch_slides_forward_never_backward_and_never_creates()
    {
        var redis = Server();
        var store = Store(redis, Prefix());
        var now = Now();
        Assert.Null(store.Touch("missing", now + 200, now + 300));
        store.TryAdd(new("id", null, now, now, now, now + 500_000));
        Assert.Equal(now + 400, store.Touch("id", now + 300, now + 400)!.IdleExpiresAtUnixMilliseconds);
        Assert.Equal(now + 400, store.Touch("id", now + 150, now + 150)!.IdleExpiresAtUnixMilliseconds);
        Assert.Equal(now + 300, store.Find("id")!.LastAccessedAtUnixMilliseconds);
    }

    [RedisFact]
    public void Remove_reports_whether_it_existed()
    {
        var redis = Server();
        var store = Store(redis, Prefix());
        var now = Now();
        store.TryAdd(new("id", null, now, now, now + 100_000, now + 200_000));
        Assert.True(store.Remove("id"));
        Assert.False(store.Remove("id"));
        Assert.Equal(0, store.Count);
    }

    [RedisFact]
    public void RemoveExpired_uses_the_earlier_deadline_inclusively_and_keeps_the_rest()
    {
        var redis = Server();
        var store = Store(redis, Prefix());
        var now = Now();
        store.TryAdd(new("expired", null, now, now, now + 100, now + 500));
        store.TryAdd(new("live", null, now, now, now + 101, now + 500));
        Assert.Equal(1, store.RemoveExpired(now + 100));
        Assert.Null(store.Find("expired"));
        Assert.NotNull(store.Find("live"));
    }

    [RedisFact]
    public void RemoveExpired_handles_more_than_one_batch()
    {
        var redis = Server();
        var store = Store(redis, Prefix());
        var now = Now();
        for (var i = 0; i < 1_025; i++) store.TryAdd(new(i.ToString(), null, now, now, now + 10, now + 20));
        Assert.Equal(1_025, store.RemoveExpired(now + 10));
        Assert.Equal(0, store.Count);
    }

    [RedisFact]
    public void Hashes_get_a_server_side_expiry_of_absolute_deadline_plus_grace()
    {
        var redis = Server();
        var prefix = Prefix(); var store = Store(redis, prefix);
        var now = Now();
        store.TryAdd(new("ttl", null, now, now, now + 30_000, now + 120_000));
        Assert.InRange(redis.Pttl(prefix + "s:ttl"), 170_000, 180_000);
    }

    [RedisFact]
    public void Unbounded_lifetime_is_clamped_for_redis_but_stored_intact()
    {
        var redis = Server();
        var prefix = Prefix(); var store = Store(redis, prefix);
        var now = Now();
        store.TryAdd(new("forever", null, now, now, long.MaxValue, long.MaxValue));
        Assert.Equal(long.MaxValue, store.Find("forever")!.AbsoluteExpiresAtUnixMilliseconds);
        Assert.True(redis.Pttl(prefix + "s:forever") > 0);
    }

    [RedisFact]
    public void Exactly_one_of_many_concurrent_adds_wins()
    {
        var redis = Server();
        var prefix = Prefix(); var stores = Enumerable.Range(0, 8).Select(_ => Store(redis, prefix)).ToArray();
        var now = Now();
        var winners = 0;
        Parallel.For(0, 64, i => { if (stores[i % stores.Length].TryAdd(new("race", null, now, now, now + 100_000, now + 200_000))) Interlocked.Increment(ref winners); });
        Assert.Equal(1, winners);
    }

    [RedisFact]
    public void Concurrent_touches_never_move_the_deadline_backwards()
    {
        var redis = Server();
        var store = Store(redis, Prefix());
        var now = Now();
        store.TryAdd(new("race", null, now, now, now, now + 500_000));
        Parallel.For(1, 100, i => store.Touch("race", now + i, now + 100 + i));
        Assert.Equal(now + 199, store.Find("race")!.IdleExpiresAtUnixMilliseconds);
    }

    [RedisFact]
    public void Session_created_on_one_replica_is_valid_terminated_and_owned_across_replicas()
    {
        var redis = Server();
        var prefix = Prefix();
        long now = Now();
        var options = new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1), 10);
        var replicaA = new FoundgineMcpSessionManager(options, Store(redis, prefix), () => now);
        var replicaB = new FoundgineMcpSessionManager(options, Store(redis, prefix), () => now);

        var created = replicaA.Create("tenant-user");
        Assert.Equal(FoundgineMcpSessionStatus.OwnerMismatch, replicaB.Validate(created.SessionId, "other-user").Status);
        Assert.Equal(FoundgineMcpSessionStatus.Valid, replicaB.Validate(created.SessionId, "tenant-user").Status);
        Assert.True(replicaB.Terminate(created.SessionId, "tenant-user"));
        Assert.False(replicaA.IsLive(created.SessionId));
    }

    [RedisFact]
    public void Idle_expiry_and_purge_are_shared_across_replicas()
    {
        var redis = Server();
        var prefix = Prefix();
        long now = Now();
        var options = new FoundgineMcpSessionOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), 10);
        var replicaA = new FoundgineMcpSessionManager(options, Store(redis, prefix), () => now);
        var replicaB = new FoundgineMcpSessionManager(options, Store(redis, prefix), () => now);

        var created = replicaA.Create();
        now += 100;
        Assert.Equal(1, replicaB.PurgeExpired());
        Assert.False(replicaA.IsLive(created.SessionId));
        Assert.Equal(0, replicaA.ActiveSessionCount);
    }

    [RedisFact]
    public void Session_cap_applies_to_the_shared_count()
    {
        var redis = Server();
        var prefix = Prefix();
        long now = Now();
        var options = new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1), 1);
        var replicaA = new FoundgineMcpSessionManager(options, Store(redis, prefix), () => now);
        var replicaB = new FoundgineMcpSessionManager(options, Store(redis, prefix), () => now);

        replicaA.Create();
        Assert.Throws<InvalidOperationException>(() => replicaB.Create());
        Assert.Equal(1, replicaB.ActiveSessionCount);
    }

    [RedisFact]
    public void Keys_are_scoped_by_prefix()
    {
        var redis = Server();
        var a = Store(redis, Prefix()); var b = Store(redis, Prefix());
        var now = Now();
        a.TryAdd(new("same", null, now, now, now + 100_000, now + 200_000));
        Assert.Null(b.Find("same"));
    }
}
