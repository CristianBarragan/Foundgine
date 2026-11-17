using Foundgine.Core.Semantic.Security.Warrants;

namespace Foundgine.Providers.Redis;

/// <summary>
/// Adds a durable external high-water anchor to a Redis-backed revocation store.
/// </summary>
/// <remarks>
/// The anchor must be shared by every host and provide an atomic, durable max operation. A successful
/// revoke is not acknowledged until its sequence has been committed to the anchor. If Redis later
/// rolls back below that value, all operations fail closed, including on a newly started process.
/// This wrapper does not make Redis itself durable and does not replace deployment-level recovery.
/// </remarks>
public sealed class AnchoredSecurityWarrantRevocationStore : IBatchedSecurityWarrantRevocationStore
{
    private readonly ISecurityWarrantRevocationStore _inner;
    private readonly ISecurityWarrantRevocationEpochAnchor _anchor;

    public AnchoredSecurityWarrantRevocationStore(
        ISecurityWarrantRevocationStore inner,
        ISecurityWarrantRevocationEpochAnchor anchor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
    }

    public long CurrentSequence => SynchronizeAnchor();

    public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now)
    {
        var revocation = _inner.Revoke(warrant, now);
        var anchoredSequence = SynchronizeAnchor();
        if (anchoredSequence < revocation.Sequence)
            throw new InvalidOperationException("The durable revocation anchor is behind the committed revocation record; failing closed.");
        return revocation;
    }

    public bool IsRevoked(string warrantId, string warrantDigest)
    {
        _ = SynchronizeAnchor();
        var result = _inner.IsRevoked(warrantId, warrantDigest);
        _ = SynchronizeAnchor();
        return result;
    }

    public bool IsDigestRevoked(string warrantDigest)
    {
        _ = SynchronizeAnchor();
        var result = _inner.IsDigestRevoked(warrantDigest);
        _ = SynchronizeAnchor();
        return result;
    }

    /// <summary>
    /// One anchor read plus one inner lookup. When the inner store can answer atomically, the lookup result and
    /// its sequence are a single snapshot, so the per-question anchor bracketing is not needed.
    /// </summary>
    public SecurityWarrantRevocationLookup Lookup(string warrantId, string warrantDigest, IReadOnlyList<string> ancestorDigests)
    {
        ArgumentNullException.ThrowIfNull(ancestorDigests);
        if (_inner is not IBatchedSecurityWarrantRevocationStore batched)
        {
            // Inner store cannot answer atomically: keep the original sequence-bracketed, per-call anchored path.
            var before = SynchronizeAnchor();
            var revoked = IsRevoked(warrantId, warrantDigest) || IsDigestRevoked(warrantDigest);
            var ancestorRevoked = false;
            if (!revoked)
            {
                foreach (var ancestor in ancestorDigests)
                {
                    if (!IsDigestRevoked(ancestor)) continue;
                    ancestorRevoked = true;
                    break;
                }
            }
            var after = SynchronizeAnchor();
            if (after != before)
                throw new InvalidOperationException("Authorization revocation state changed during warrant validation.");
            return new SecurityWarrantRevocationLookup(after, revoked, ancestorRevoked);
        }

        var durableBefore = _anchor.ReadHighWaterMark();
        if (durableBefore < 0)
            throw new InvalidOperationException("The durable revocation anchor returned a negative high-water mark.");

        var lookup = batched.Lookup(warrantId, warrantDigest, ancestorDigests);
        _ = EnsureAnchored(durableBefore, lookup.Sequence);
        return lookup;
    }

    private long SynchronizeAnchor()
    {
        var durableBefore = _anchor.ReadHighWaterMark();
        if (durableBefore < 0)
            throw new InvalidOperationException("The durable revocation anchor returned a negative high-water mark.");

        return EnsureAnchored(durableBefore, _inner.CurrentSequence);
    }

    private long EnsureAnchored(long durableBefore, long observed)
    {
        if (observed < durableBefore)
            throw new InvalidOperationException(
                $"Redis revocation sequence {observed} is below durable high-water mark {durableBefore}; failing closed after possible state rollback.");

        // Steady state: Redis and the durable anchor agree, so there is nothing to persist. Skipping the write
        // (a synchronous_commit transaction on one shared row) keeps authorization reads from serializing on it.
        // Rollback (observed < durable) was already rejected above; a concurrent advance is caught by the next call.
        if (observed == durableBefore)
            return observed;

        _anchor.AdvanceHighWaterMark(observed);
        var durableAfter = _anchor.ReadHighWaterMark();
        if (durableAfter < observed)
            throw new InvalidOperationException("The durable revocation anchor did not persist the observed sequence; failing closed.");
        if (observed < durableAfter)
            throw new InvalidOperationException(
                $"Redis revocation sequence {observed} is behind concurrently advanced durable high-water mark {durableAfter}; failing closed.");

        return observed;
    }
}
