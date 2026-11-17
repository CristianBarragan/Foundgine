namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Durable, shared high-water mark for warrant revocation sequence numbers.
/// </summary>
/// <remarks>
/// Implementations must provide an atomic monotonic max operation across all Foundgine hosts.
/// The value must survive process and Redis-primary loss and must not be reset during ordinary
/// recovery. A local in-memory implementation does not satisfy this contract.
/// </remarks>
public interface ISecurityWarrantRevocationEpochAnchor
{
    /// <summary>Reads the authoritative durable high-water mark. A new installation may return zero.</summary>
    long ReadHighWaterMark();

    /// <summary>Atomically stores max(current value, sequence) and durably commits it before returning.</summary>
    void AdvanceHighWaterMark(long sequence);
}
