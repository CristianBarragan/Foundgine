using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Providers.Redis;
using Xunit;

namespace Foundgine.Redis.Tests;

public sealed class AnchoredSecurityWarrantRevocationStoreTests
{
    private static SecurityWarrant Warrant(string id) => new(
        id, "issuer", "subject", "audience", [new CapabilityGrant("read", "query")],
        SecurityWarrantConstraints.Unrestricted, DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddHours(1), "nonce-" + id, "key-1", null, []);

    [Fact]
    public void Fresh_host_detects_redis_rollback_using_durable_anchor()
    {
        var anchor = new TestAnchor(8);
        var inner = new FixedSequenceStore(7);
        var store = new AnchoredSecurityWarrantRevocationStore(inner, anchor);

        var error = Assert.Throws<InvalidOperationException>(() => _ = store.CurrentSequence);
        Assert.Contains("below durable high-water mark", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, anchor.Value);
    }

    [Fact]
    public void Revoke_is_not_returned_until_sequence_is_anchored()
    {
        var inner = new MemorySecurityWarrantRevocationStore();
        var anchor = new TestAnchor(0);
        var store = new AnchoredSecurityWarrantRevocationStore(inner, anchor);
        var warrant = Warrant("anchored");

        var record = store.Revoke(warrant, DateTimeOffset.UtcNow);

        Assert.Equal(record.Sequence, anchor.Value);
        Assert.True(store.IsRevoked(warrant.Id, warrant.Digest));
    }

    [Fact]
    public void Anchor_failure_fails_closed_for_sequence_and_revocation()
    {
        var anchor = new TestAnchor(0) { Fail = true };
        var store = new AnchoredSecurityWarrantRevocationStore(new MemorySecurityWarrantRevocationStore(), anchor);

        Assert.Throws<IOException>(() => _ = store.CurrentSequence);
        Assert.Throws<IOException>(() => store.Revoke(Warrant("anchor-down"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Concurrently_advanced_anchor_rejects_a_stale_redis_observation()
    {
        var anchor = new AdvancingAnchor(0, 9);
        var store = new AnchoredSecurityWarrantRevocationStore(new FixedSequenceStore(8), anchor);

        var error = Assert.Throws<InvalidOperationException>(() => _ = store.CurrentSequence);
        Assert.Contains("concurrently advanced", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestAnchor(long value) : ISecurityWarrantRevocationEpochAnchor
    {
        public long Value { get; private set; } = value;
        public bool Fail { get; set; }
        public long ReadHighWaterMark() { if (Fail) throw new IOException("anchor unavailable"); return Value; }
        public void AdvanceHighWaterMark(long sequence)
        {
            if (Fail) throw new IOException("anchor unavailable");
            Value = Math.Max(Value, sequence);
        }
    }

    private sealed class AdvancingAnchor(long value, long advanceTo) : ISecurityWarrantRevocationEpochAnchor
    {
        private long _value = value;
        public long ReadHighWaterMark() => _value;
        public void AdvanceHighWaterMark(long sequence) => _value = Math.Max(Math.Max(_value, sequence), advanceTo);
    }

    private sealed class FixedSequenceStore(long sequence) : ISecurityWarrantRevocationStore
    {
        public long CurrentSequence => sequence;
        public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now) =>
            new(warrant.Id, warrant.Digest, now, sequence);
        public bool IsRevoked(string warrantId, string warrantDigest) => false;
        public bool IsDigestRevoked(string warrantDigest) => false;
    }
}
