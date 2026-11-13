using System.Security.Cryptography;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>Atomic, host-owned single-use store for approval ids.</summary>
public interface IFoundgineMcpApprovalTokenReplayStore
{
    /// <summary>Atomically records the id until expiry; false means it was already consumed.</summary>
    bool TryConsume(string approvalId, long expiresAtUnixMilliseconds);
}

/// <summary>Enforces bounded token age, clock skew and one-time use after signature verification.</summary>
public sealed class FoundgineMcpApprovalTokenPolicy
{
    private readonly TimeSpan _maximumAge;
    private readonly TimeSpan _allowedFutureSkew;
    private readonly Func<long> _clock;
    private readonly IFoundgineMcpApprovalTokenReplayStore _replayStore;

    public FoundgineMcpApprovalTokenPolicy(
        IFoundgineMcpApprovalTokenReplayStore replayStore,
        TimeSpan? maximumAge = null,
        TimeSpan? allowedFutureSkew = null,
        Func<long>? unixMillisecondsClock = null)
    {
        _replayStore = replayStore ?? throw new ArgumentNullException(nameof(replayStore));
        _maximumAge = maximumAge ?? TimeSpan.FromMinutes(5);
        _allowedFutureSkew = allowedFutureSkew ?? TimeSpan.FromSeconds(30);
        if (_maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        if (_allowedFutureSkew < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(allowedFutureSkew));
        _clock = unixMillisecondsClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>Must be called only after signature verification and before provider execution.</summary>
    public void ValidateAndConsume(FoundgineMcpApprovalTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var now = _clock();
        var approvedAt = claims.ApprovedAtUnixMilliseconds;
        var futureLimit = SaturatingAdd(now, checked((long)_allowedFutureSkew.TotalMilliseconds));
        if (approvedAt > futureLimit) throw new CryptographicException("MCP approval token is not yet valid.");
        var expiresAt = SaturatingAdd(approvedAt, checked((long)_maximumAge.TotalMilliseconds));
        if (now >= expiresAt) throw new CryptographicException("MCP approval token has expired.");
        if (!_replayStore.TryConsume(claims.ApprovalId, expiresAt))
            throw new CryptographicException("MCP approval token has already been consumed.");
    }

    private static long SaturatingAdd(long a, long b)
    {
        try { return checked(a + b); }
        catch (OverflowException) { return b >= 0 ? long.MaxValue : long.MinValue; }
    }
}
