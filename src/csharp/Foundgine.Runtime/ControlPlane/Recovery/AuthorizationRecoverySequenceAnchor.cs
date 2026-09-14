namespace Foundgine.Runtime.ControlPlane;

/// <summary>
/// Durable monotonic anchor for recovery checkpoints. In production this must be backed
/// by storage whose rollback/fork guarantees are stronger than the PostgreSQL database being
/// protected (for example a managed KMS/HSM, append-only ledger, or independent control plane).
/// </summary>
public interface IAuthorizationRecoverySequenceAnchor
{
    ValueTask<long> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> AdvanceAsync(long sequence, CancellationToken cancellationToken = default);
}

/// <summary>Test/reference anchor. Production deployments must replace this with independent durable storage.</summary>
public sealed class InMemoryAuthorizationRecoverySequenceAnchor : IAuthorizationRecoverySequenceAnchor
{
    private long _sequence;

    public ValueTask<long> ReadAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Interlocked.Read(ref _sequence));

    public ValueTask<bool> AdvanceAsync(long sequence, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _sequence);
            if (sequence <= current)
                return ValueTask.FromResult(false);
            if (Interlocked.CompareExchange(ref _sequence, sequence, current) == current)
                return ValueTask.FromResult(true);
        }
    }
}