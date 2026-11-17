using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Providers.Redis;
using Xunit;
using Xunit.Sdk;

namespace Foundgine.Redis.Tests;

public sealed class RedisSecurityWarrantRevocationStoreTests
{
    private static SecurityWarrant Warrant(string id, string nonce) => new(
        id, "issuer", "subject", "audience", [new CapabilityGrant("read", "query")],
        SecurityWarrantConstraints.Unrestricted, DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddHours(1), nonce, "key-1", null, []);

    [Fact]
    public void Backend_failure_fails_closed_for_reads_and_writes()
    {
        var store = new RedisSecurityWarrantRevocationStore(new DownBackend());
        Assert.Throws<InvalidOperationException>(() => _ = store.CurrentSequence);
        Assert.Throws<InvalidOperationException>(() => store.IsDigestRevoked("digest"));
        Assert.Throws<InvalidOperationException>(() => store.Revoke(Warrant("w", "n"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Sequence_regression_after_primary_rollback_fails_closed()
    {
        var backend = new SequenceBackend(10);
        var store = new RedisSecurityWarrantRevocationStore(backend);

        Assert.Equal(10, store.CurrentSequence);
        backend.Sequence = 9;

        var error = Assert.Throws<InvalidOperationException>(() => _ = store.CurrentSequence);
        Assert.Contains("sequence regressed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Out_of_order_completion_of_concurrent_calls_is_not_a_rollback()
    {
        var backend = new GatedBackend();
        var store = new RedisSecurityWarrantRevocationStore(backend);

        // Call A reaches Redis first (value 47) but is delayed; call B reaches Redis second (value 48) and returns first.
        var a = Task.Run(() => store.CurrentSequence);
        Assert.True(backend.FirstCallEntered.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(48, store.CurrentSequence);
        backend.ReleaseFirstCall.Set();

        Assert.Equal(47, await a);
        // The high-water mark stays at 48, so a genuine later rollback is still detected.
        backend.Next = 47;
        Assert.Throws<InvalidOperationException>(() => _ = store.CurrentSequence);
    }

    [Fact]
    public void Readiness_probe_requires_a_successful_write_read_round_trip()
    {
        var backend = new ProbeBackend(success: true, sequence: 17);
        var store = new RedisSecurityWarrantRevocationStore(backend);

        Assert.Equal(17, store.Probe());
        Assert.Equal(17, store.CurrentSequence);
    }

    [Fact]
    public void Readiness_probe_fails_closed_when_write_verification_fails()
    {
        var store = new RedisSecurityWarrantRevocationStore(new ProbeBackend(success: false, sequence: 0));
        Assert.Throws<InvalidOperationException>(() => store.Probe());
    }

    [Fact]
    public void Prefix_cannot_override_the_shared_cluster_hash_tag()
    {
        Assert.Throws<ArgumentException>(() => new RedisSecurityWarrantRevocationStore(new DownBackend(), "tenant:{other}:"));
    }

    [Fact]
    public void Blank_digest_is_rejected_before_backend_access()
    {
        var backend = new DownBackend();
        var store = new RedisSecurityWarrantRevocationStore(backend);
        Assert.Throws<ArgumentException>(() => store.IsDigestRevoked(" "));
        Assert.Throws<ArgumentException>(() => store.IsRevoked("w", " "));
    }

    [RedisFact]
    public void Revocation_is_shared_idempotent_and_monotonic_across_store_instances()
    {
        var redis = RespRedis.FromEnvironment() ?? throw SkipException.ForSkip("Set FOUNDGINE_REDIS to run live Redis integration tests.");
        var prefix = $"foundgine:test:{Guid.NewGuid():N}";
        var first = new RedisSecurityWarrantRevocationStore(redis, prefix);
        var second = new RedisSecurityWarrantRevocationStore(redis, prefix);
        var warrant = Warrant("shared-warrant", "nonce");

        Assert.Equal(0, first.CurrentSequence);
        var revoked = first.Revoke(warrant, DateTimeOffset.UtcNow);
        var repeated = second.Revoke(warrant, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(revoked, repeated);
        Assert.Equal(2, second.CurrentSequence); // Repeated revoke advances the observation sequence.
        Assert.True(second.IsRevoked(warrant.Id, warrant.Digest));
        Assert.True(first.IsDigestRevoked(warrant.Digest));
        Assert.False(first.IsRevoked("different-id", warrant.Digest));
    }

    [RedisFact]
    public void Concurrent_distinct_revocations_receive_unique_monotonic_sequences()
    {
        var redis = RespRedis.FromEnvironment() ?? throw SkipException.ForSkip("Set FOUNDGINE_REDIS to run live Redis integration tests.");
        var prefix = $"foundgine:test:{Guid.NewGuid():N}";
        var stores = Enumerable.Range(0, 8).Select(_ => new RedisSecurityWarrantRevocationStore(redis, prefix)).ToArray();
        var records = new SecurityWarrantRevocation[64];
        Parallel.For(0, records.Length, i => records[i] = stores[i % stores.Length].Revoke(
            Warrant("w-" + i, "n-" + i), DateTimeOffset.UtcNow));

        Assert.Equal(64, records.Select(x => x.Sequence).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 64).Select(x => (long)x), records.Select(x => x.Sequence).Order());
        Assert.Equal(64, stores[0].CurrentSequence);
    }

    private sealed class SequenceBackend(long sequence) : IRedisScriptBackend
    {
        public long Sequence { get; set; } = sequence;

        public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args) =>
            [Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }

    private sealed class GatedBackend : IRedisScriptBackend
    {
        private int _calls;
        public long Next { get; set; } = 48;
        public ManualResetEventSlim FirstCallEntered { get; } = new();
        public ManualResetEventSlim ReleaseFirstCall { get; } = new();

        public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstCallEntered.Set();
                Assert.True(ReleaseFirstCall.Wait(TimeSpan.FromSeconds(10)));
                return ["47"];
            }

            return [Next.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        }
    }

    private sealed class ProbeBackend(bool success, long sequence) : IRedisScriptBackend
    {
        public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args) =>
            keys.Count == 1
                ? [sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)]
                : [success ? "1" : "0", sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }

    private sealed class DownBackend : IRedisScriptBackend
    {
        public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args) =>
            throw new IOException("connection refused");
    }
}