namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Immutable optimistic-concurrency snapshot for a delegation parent.</summary>
public sealed record SecurityWarrantDelegationConcurrencySnapshot(
    string ParentWarrantId,
    string ParentWarrantDigest,
    long Sequence);

/// <summary>Result of atomically reserving one child slot under a parent warrant.</summary>
public sealed record SecurityWarrantDelegationReservation(
    string ParentWarrantId,
    string ParentWarrantDigest,
    string ChildWarrantId,
    string ChildWarrantDigest,
    string ChildNonce,
    long Sequence);

/// <summary>
/// Serializes delegation issuance per exact parent warrant. Different children may
/// legitimately fork from the same parent, but two writers cannot commit the same
/// child identity/nonce or both commit from the same stale parent sequence.
/// </summary>
public interface ISecurityWarrantDelegationConcurrencyStore
{
    SecurityWarrantDelegationConcurrencySnapshot Capture(SecurityWarrant parent);

    SecurityWarrantDelegationReservation CommitChild(
        SecurityWarrant parent,
        SecurityWarrant child,
        SecurityWarrantDelegationConcurrencySnapshot expected);

    bool IsCommitted(SecurityWarrant child);
}

/// <summary>In-memory adversarial implementation used to model the atomic database boundary.</summary>
public sealed class MemorySecurityWarrantDelegationConcurrencyStore
    : ISecurityWarrantDelegationConcurrencyStore
{
    private sealed class ParentState
    {
        public readonly object Gate = new();
        public long Sequence;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ParentState> _parents =
        new(StringComparer.Ordinal);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SecurityWarrantDelegationReservation>
        _children = new(StringComparer.Ordinal);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SecurityWarrantDelegationReservation>
        _nonces = new(StringComparer.Ordinal);

    public SecurityWarrantDelegationConcurrencySnapshot Capture(SecurityWarrant parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var state = _parents.GetOrAdd(Key(parent), static _ => new ParentState());
        lock (state.Gate)
            return Snapshot(parent, state.Sequence);
    }

    public SecurityWarrantDelegationReservation CommitChild(
        SecurityWarrant parent,
        SecurityWarrant child,
        SecurityWarrantDelegationConcurrencySnapshot expected)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(expected);

        SecurityWarrantDelegationChainValidator.Validate([parent, child], child.IssuedAt);

        // Chain validation only proves shape/identity/ancestry. It does not prove the
        // child stays within the parent's authority, so the existing non-escalation
        // rules (constraints, grants, expiry, ...) must still be enforced here before
        // a child slot is ever reserved.
        SecurityWarrantAttenuator.Attenuate(parent, child, child.IssuedAt);

        var parentKey = Key(parent);
        if (!StringComparer.Ordinal.Equals(expected.ParentWarrantId, parent.Id) ||
            !StringComparer.Ordinal.Equals(expected.ParentWarrantDigest, parent.Digest))
            throw new InvalidOperationException("Delegation concurrency snapshot is bound to a different parent.");

        var state = _parents.GetOrAdd(parentKey, static _ => new ParentState());
        lock (state.Gate)
        {
            if (state.Sequence != expected.Sequence)
                throw new InvalidOperationException(
                    "Delegation parent changed concurrently; the child must be retried from a fresh parent sequence.");

            var childKey = Key(child);
            if (_children.ContainsKey(childKey))
                throw new InvalidOperationException("The exact child warrant has already been committed.");

            var nonceKey = parent.Id + "\u001f" + parent.Digest + "\u001f" + child.Nonce;
            if (_nonces.ContainsKey(nonceKey))
                throw new InvalidOperationException("The child nonce has already been committed under this parent.");

            var sequence = checked(++state.Sequence);
            var reservation = new SecurityWarrantDelegationReservation(
                parent.Id, parent.Digest, child.Id, child.Digest, child.Nonce, sequence);
            if (!_children.TryAdd(childKey, reservation) || !_nonces.TryAdd(nonceKey, reservation))
                throw new InvalidOperationException(
                    "Concurrent delegation fork detected; child reservation was not committed.");
            return reservation;
        }
    }

    public bool IsCommitted(SecurityWarrant child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return _children.ContainsKey(Key(child));
    }

    private static SecurityWarrantDelegationConcurrencySnapshot Snapshot(SecurityWarrant parent, long sequence) =>
        new(parent.Id, parent.Digest, sequence);

    private static string Key(SecurityWarrant warrant) => warrant.Id + "\u001f" + warrant.Digest;
}

/// <summary>Final execution guard for a parent whose delegation state was snapshotted.</summary>
public static class SecurityWarrantDelegationConcurrencyGuard
{
    public static SecurityWarrantDelegationConcurrencySnapshot Capture(
        ISecurityWarrantDelegationConcurrencyStore store,
        SecurityWarrant parent)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(parent);
        return store.Capture(parent);
    }

    public static SecurityWarrantDelegationReservation CommitChild(
        ISecurityWarrantDelegationConcurrencyStore store,
        SecurityWarrant parent,
        SecurityWarrant child,
        SecurityWarrantDelegationConcurrencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.CommitChild(parent, child, snapshot);
    }
}