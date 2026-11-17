namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Records a revoked warrant digest. Revocation is monotonic and cannot be undone.</summary>
public sealed record SecurityWarrantRevocation(
    string WarrantId,
    string WarrantDigest,
    DateTimeOffset RevokedAt,
    long Sequence);

/// <summary>Monotonic revocation authority used at execution time.</summary>
public interface ISecurityWarrantRevocationStore
{
    long CurrentSequence { get; }
    SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now);
    bool IsRevoked(string warrantId, string warrantDigest);
    bool IsDigestRevoked(string warrantDigest);
}

/// <summary>Result of one atomic revocation lookup: the observed sequence and what was found at that sequence.</summary>
public readonly record struct SecurityWarrantRevocationLookup(long Sequence, bool Revoked, bool AncestorRevoked);

/// <summary>
/// Optional capability: answers the warrant, digest and delegation-ancestor revocation questions together
/// with the sequence they were observed at, as one consistent snapshot. Remote stores can implement this in a
/// single round trip instead of one call per question plus sequence bracketing.
/// </summary>
public interface IBatchedSecurityWarrantRevocationStore : ISecurityWarrantRevocationStore
{
    SecurityWarrantRevocationLookup Lookup(string warrantId, string warrantDigest, IReadOnlyList<string> ancestorDigests);
}

/// <summary>Thread-safe in-memory implementation for security tests and deterministic execution tests.</summary>
public sealed class MemorySecurityWarrantRevocationStore : ISecurityWarrantRevocationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SecurityWarrantRevocation> _revoked = new(StringComparer.Ordinal);
    private long _sequence;

    public long CurrentSequence
    {
        get { lock (_gate) return _sequence; }
    }

    public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        if (string.IsNullOrWhiteSpace(warrant.Id) || string.IsNullOrWhiteSpace(warrant.Digest))
            throw new InvalidOperationException("A warrant must have an identity and digest before revocation.");

        lock (_gate)
        {
            var key = warrant.Id + "\u001f" + warrant.Digest;
            var sequence = ++_sequence;
            if (!_revoked.TryGetValue(key, out var entry))
            {
                entry = new SecurityWarrantRevocation(warrant.Id, warrant.Digest, now, sequence);
                _revoked.Add(key, entry);
            }
            return entry;
        }
    }

    public bool IsRevoked(string warrantId, string warrantDigest)
    {
        lock (_gate) return _revoked.ContainsKey(warrantId + "\u001f" + warrantDigest);
    }

    public bool IsDigestRevoked(string warrantDigest)
    {
        lock (_gate) return _revoked.Values.Any(x => StringComparer.Ordinal.Equals(x.WarrantDigest, warrantDigest));
    }
}

/// <summary>Immutable execution-time view of revocation state.</summary>
public readonly record struct SecurityWarrantRevocationSnapshot(long Sequence)
{
    public static SecurityWarrantRevocationSnapshot Capture(ISecurityWarrantRevocationStore store) =>
        new(store.CurrentSequence);

    public void AssertUnchanged(ISecurityWarrantRevocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.CurrentSequence != Sequence)
            throw new InvalidOperationException("Authorization revocation state changed during execution.");
    }

    /// <summary>
    /// Compares against a snapshot returned by a fresh, bracketed <see cref="SecurityWarrantRevocationGuard.Validate"/>,
    /// avoiding a second round of store reads just to re-observe the sequence.
    /// </summary>
    public void AssertUnchanged(SecurityWarrantRevocationSnapshot current)
    {
        if (current.Sequence != Sequence)
            throw new InvalidOperationException("Authorization revocation state changed during execution.");
    }
}

/// <summary>
/// Rejects a warrant when the warrant itself or any delegated ancestor has been revoked.
/// A child cannot outlive the authority from which it was delegated.
/// </summary>
public static class SecurityWarrantRevocationGuard
{
    public static SecurityWarrantRevocationSnapshot Validate(
        SecurityWarrant warrant,
        ISecurityWarrantRevocationStore store,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        ArgumentNullException.ThrowIfNull(store);
        if (!warrant.IsTimeValid(now))
            throw new InvalidOperationException("Security warrant is expired or not yet valid.");

        if (store is IBatchedSecurityWarrantRevocationStore batched)
        {
            // The lookup is one consistent snapshot, so no separate before/after sequence bracket is needed.
            var lookup = batched.Lookup(warrant.Id, warrant.Digest, warrant.DelegationPath);
            if (lookup.Revoked)
                throw new InvalidOperationException("Security warrant has been revoked.");
            if (lookup.AncestorRevoked)
                throw new InvalidOperationException("A parent security warrant has been revoked.");
            return new SecurityWarrantRevocationSnapshot(lookup.Sequence);
        }

        // Bracket all revocation reads with the monotonic sequence. If a revocation races any
        // individual lookup, the sequence changes and this validation fails closed. Capturing
        // only after the lookups would leave a check-then-capture race window.
        var sequenceBefore = store.CurrentSequence;

        if (store.IsRevoked(warrant.Id, warrant.Digest) || store.IsDigestRevoked(warrant.Digest))
            throw new InvalidOperationException("Security warrant has been revoked.");

        foreach (var ancestorDigest in warrant.DelegationPath)
        {
            if (store.IsDigestRevoked(ancestorDigest))
                throw new InvalidOperationException("A parent security warrant has been revoked.");
        }

        var sequenceAfter = store.CurrentSequence;
        if (sequenceAfter != sequenceBefore)
            throw new InvalidOperationException("Authorization revocation state changed during warrant validation.");

        return new SecurityWarrantRevocationSnapshot(sequenceAfter);
    }
}
