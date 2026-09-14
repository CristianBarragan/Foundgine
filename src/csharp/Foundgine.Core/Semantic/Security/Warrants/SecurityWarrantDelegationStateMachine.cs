namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Linearizable lifecycle states for delegation authority.</summary>
public enum SecurityWarrantDelegationState
{
    Active = 0,
    Revoked = 1,
    Compromised = 2
}

/// <summary>Immutable state observed at one linearization point.</summary>
public sealed record SecurityWarrantDelegationStateSnapshot(
    string WarrantId,
    string WarrantDigest,
    SecurityWarrantDelegationState State,
    long Sequence,
    string? ActiveKeyId);

/// <summary>Atomic state transition result.</summary>
public sealed record SecurityWarrantDelegationTransition(
    SecurityWarrantDelegationStateSnapshot Before,
    SecurityWarrantDelegationStateSnapshot After,
    string Operation);

/// <summary>
/// Small, provider-neutral state machine used to define the legal lifecycle boundary.
/// The lock is the model of the database serialization boundary; production stores must
/// provide an equivalent atomic compare-and-transition operation.
/// </summary>
public sealed class SecurityWarrantDelegationStateMachine
{
    private sealed class Cell
    {
        public readonly object Gate = new();
        public SecurityWarrantDelegationState State = SecurityWarrantDelegationState.Active;
        public long Sequence;
        public string? ActiveKeyId;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Cell> _cells =
        new(StringComparer.Ordinal);

    public SecurityWarrantDelegationStateSnapshot Register(SecurityWarrant warrant, string? activeKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        var cell = _cells.GetOrAdd(Key(warrant), _ => new Cell { ActiveKeyId = activeKeyId });
        lock (cell.Gate)
        {
            if (cell.Sequence != 0)
                throw new InvalidOperationException("Warrant is already registered in the delegation state machine.");
            cell.Sequence = 1;
            cell.ActiveKeyId = activeKeyId;
            return Snapshot(warrant, cell);
        }
    }

    public SecurityWarrantDelegationTransition Revoke(SecurityWarrant warrant)
        => Transition(warrant, "revoke", static s => s == SecurityWarrantDelegationState.Active,
            static cell => cell.State = SecurityWarrantDelegationState.Revoked);

    public SecurityWarrantDelegationTransition Compromise(SecurityWarrant warrant)
        => Transition(warrant, "compromise", static s => s != SecurityWarrantDelegationState.Compromised,
            static cell => cell.State = SecurityWarrantDelegationState.Compromised);

    /// <summary>Rotates the issuer key without changing authority state.</summary>
    public SecurityWarrantDelegationTransition RotateKey(SecurityWarrant warrant, string newKeyId)
    {
        if (string.IsNullOrWhiteSpace(newKeyId)) throw new ArgumentException("Key id is required.", nameof(newKeyId));
        return Transition(warrant, "rotate-key", static s => s == SecurityWarrantDelegationState.Active,
            cell => cell.ActiveKeyId = newKeyId);
    }

    /// <summary>
    /// Re-delegation is allowed only from an active, non-compromised parent and therefore
    /// must observe the same linearization point as revocation/compromise/key rotation.
    /// </summary>
    public SecurityWarrantDelegationStateSnapshot AssertCanDelegate(SecurityWarrant parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var cell = Get(parent);
        lock (cell.Gate)
        {
            if (cell.State != SecurityWarrantDelegationState.Active)
                throw new InvalidOperationException("A revoked or compromised warrant cannot delegate.");
            return Snapshot(parent, cell);
        }
    }

    public SecurityWarrantDelegationStateSnapshot Read(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        var cell = Get(warrant);
        lock (cell.Gate) return Snapshot(warrant, cell);
    }

    private SecurityWarrantDelegationTransition Transition(
        SecurityWarrant warrant,
        string operation,
        Func<SecurityWarrantDelegationState, bool> allowed,
        Action<Cell> apply)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        var cell = Get(warrant);
        lock (cell.Gate)
        {
            var before = Snapshot(warrant, cell);
            if (!allowed(cell.State))
                throw new InvalidOperationException(
                    $"Illegal delegation state transition '{operation}' from {cell.State}.");
            apply(cell);
            cell.Sequence = checked(cell.Sequence + 1);
            var after = Snapshot(warrant, cell);
            return new(before, after, operation);
        }
    }

    private Cell Get(SecurityWarrant warrant)
    {
        var key = Key(warrant);
        if (!_cells.TryGetValue(key, out var cell))
            throw new InvalidOperationException("Warrant is not registered in the delegation state machine.");
        return cell;
    }

    private static SecurityWarrantDelegationStateSnapshot Snapshot(SecurityWarrant warrant, Cell cell) =>
        new(warrant.Id, warrant.Digest, cell.State, cell.Sequence, cell.ActiveKeyId);

    private static string Key(SecurityWarrant warrant) => warrant.Id + "\u001f" + warrant.Digest;
}