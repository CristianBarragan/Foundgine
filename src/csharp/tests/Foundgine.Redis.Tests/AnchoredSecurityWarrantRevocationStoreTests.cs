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
    
    [Fact]
    public void Steady_state_reads_do_not_write_to_the_anchor()
    {
        var anchor = new TestAnchor(5);
        var store = new AnchoredSecurityWarrantRevocationStore(new FixedSequenceStore(5), anchor);

        Assert.Equal(5, store.CurrentSequence);
        Assert.Equal(5, store.CurrentSequence);

        Assert.Equal(0, anchor.AdvanceCalls);
    }

    [Fact]
    public void Newer_redis_sequence_is_persisted_to_the_anchor()
    {
        var anchor = new TestAnchor(5);
        var store = new AnchoredSecurityWarrantRevocationStore(new FixedSequenceStore(6), anchor);

        Assert.Equal(6, store.CurrentSequence);

        Assert.Equal(6, anchor.Value);
        Assert.Equal(1, anchor.AdvanceCalls);
    }

    [Fact]
    public void Batched_lookup_in_steady_state_is_one_anchor_read_one_lookup_and_no_write()
    {
        var anchor = new TestAnchor(5);
        var inner = new BatchedStore(5);
        var store = new AnchoredSecurityWarrantRevocationStore(inner, anchor);

        var lookup = store.Lookup("id", "digest", ["ancestor"]);

        Assert.Equal(5, lookup.Sequence);
        Assert.False(lookup.Revoked);
        Assert.Equal(1, anchor.ReadCalls);
        Assert.Equal(0, anchor.AdvanceCalls);
        Assert.Equal(1, inner.LookupCalls);
        Assert.Equal(0, inner.PerQuestionCalls);
    }

    [Fact]
    public void Batched_lookup_detects_redis_rollback_using_durable_anchor()
    {
        var store = new AnchoredSecurityWarrantRevocationStore(new BatchedStore(7), new TestAnchor(8));

        var error = Assert.Throws<InvalidOperationException>(() => store.Lookup("id", "digest", []));
        Assert.Contains("below durable high-water mark", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Batched_lookup_persists_a_newer_redis_sequence()
    {
        var anchor = new TestAnchor(5);
        var store = new AnchoredSecurityWarrantRevocationStore(new BatchedStore(6), anchor);

        Assert.Equal(6, store.Lookup("id", "digest", []).Sequence);

        Assert.Equal(6, anchor.Value);
        Assert.Equal(1, anchor.AdvanceCalls);
    }

    [Fact]
    public void Guard_rejects_revoked_warrant_through_the_batched_path()
    {
        var store = new AnchoredSecurityWarrantRevocationStore(new BatchedStore(5, revoked: true), new TestAnchor(5));

        var error = Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantRevocationGuard.Validate(Warrant("revoked"), store, DateTimeOffset.UtcNow));
        Assert.Contains("has been revoked", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guard_rejects_revoked_ancestor_through_the_batched_path()
    {
        var store = new AnchoredSecurityWarrantRevocationStore(new BatchedStore(5, ancestorRevoked: true), new TestAnchor(5));

        var error = Assert.Throws<InvalidOperationException>(() =>
            SecurityWarrantRevocationGuard.Validate(Warrant("child"), store, DateTimeOffset.UtcNow));
        Assert.Contains("parent security warrant", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guard_returns_the_lookup_sequence_as_the_snapshot()
    {
        var store = new AnchoredSecurityWarrantRevocationStore(new BatchedStore(5), new TestAnchor(5));

        var snapshot = SecurityWarrantRevocationGuard.Validate(Warrant("ok"), store, DateTimeOffset.UtcNow);

        Assert.Equal(5, snapshot.Sequence);
    }

    private sealed class TestAnchor(long initialValue) : ISecurityWarrantRevocationEpochAnchor
    {
        public long Value { get; private set; } = initialValue;
        public bool Fail { get; set; }
        public int AdvanceCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public long ReadHighWaterMark()
        {
            ReadCalls++;
            return Fail ? throw new IOException("anchor unavailable") : Value;
        }

        public void AdvanceHighWaterMark(long sequence)
        {
            AdvanceCalls++;
            if (Fail)
                throw new IOException("anchor unavailable");
            if (sequence > Value)
                Value = sequence;
        }
    }


    private sealed class AdvancingAnchor(long value, long advanceTo) : ISecurityWarrantRevocationEpochAnchor
    {
        private long _value = value;
        public long ReadHighWaterMark() => _value;
        public void AdvanceHighWaterMark(long sequence) => _value = Math.Max(Math.Max(_value, sequence), advanceTo);
    }

    private sealed class BatchedStore(long sequence, bool revoked = false, bool ancestorRevoked = false)
        : IBatchedSecurityWarrantRevocationStore
    {
        public int LookupCalls { get; private set; }
        public int PerQuestionCalls { get; private set; }

        public long CurrentSequence
        {
            get { PerQuestionCalls++; return sequence; }
        }

        public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now) =>
            new(warrant.Id, warrant.Digest, now, sequence);

        public bool IsRevoked(string warrantId, string warrantDigest) { PerQuestionCalls++; return false; }
        public bool IsDigestRevoked(string warrantDigest) { PerQuestionCalls++; return false; }

        public SecurityWarrantRevocationLookup Lookup(string warrantId, string warrantDigest, IReadOnlyList<string> ancestorDigests)
        {
            LookupCalls++;
            return new SecurityWarrantRevocationLookup(sequence, revoked, ancestorRevoked);
        }
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
