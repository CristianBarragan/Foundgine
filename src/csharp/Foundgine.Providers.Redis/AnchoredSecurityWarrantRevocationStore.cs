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
public sealed class AnchoredSecurityWarrantRevocationStore : ISecurityWarrantRevocationStore
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

    private long SynchronizeAnchor()
    {
        var durableBefore = _anchor.ReadHighWaterMark();
        if (durableBefore < 0)
            throw new InvalidOperationException("The durable revocation anchor returned a negative high-water mark.");

        var observed = _inner.CurrentSequence;
        if (observed < durableBefore)
            throw new InvalidOperationException(
                $"Redis revocation sequence {observed} is below durable high-water mark {durableBefore}; failing closed after possible state rollback.");

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
