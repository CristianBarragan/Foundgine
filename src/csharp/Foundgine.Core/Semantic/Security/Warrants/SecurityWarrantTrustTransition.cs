using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Lifecycle state of a delegation issuer signing key.</summary>
public enum DelegationIssuerKeyState
{
    Active = 0,
    VerificationOnly = 1,
    Retired = 2
}

/// <summary>Atomic execution-time snapshot of issuer trust and issuer-key lifecycle state.</summary>
public sealed record DelegationIssuerTrustSnapshot(
    string Issuer,
    long Sequence,
    string TrustFingerprint,
    string KeyId,
    DelegationIssuerKeyState KeyState)
{
    public void AssertCurrent(ISecurityWarrantDelegationTrustStateResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var current = resolver.Capture(Issuer, KeyId);
        if (current.Sequence != Sequence ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(current.TrustFingerprint),
                Convert.FromHexString(TrustFingerprint)) ||
            current.KeyState != KeyState)
            throw new InvalidOperationException("Delegation issuer trust or key lifecycle changed during execution.");
    }
}

/// <summary>
/// Provides a versioned, execution-time view of delegation trust. Implementations must
/// publish trust and key-state changes atomically under one monotonically increasing sequence.
/// </summary>
public interface ISecurityWarrantDelegationTrustStateResolver : ISecurityWarrantDelegationTrustResolver
{
    long CurrentSequence { get; }
    DelegationIssuerTrustSnapshot Capture(string issuer, string keyId);
}

/// <summary>In-memory reference implementation used by deterministic security tests.</summary>
public sealed class MemorySecurityWarrantDelegationTrustStateStore : ISecurityWarrantDelegationTrustStateResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DelegationIssuerTrust> _trust = new(StringComparer.Ordinal);
    private long _sequence;

    public long CurrentSequence
    {
        get
        {
            lock (_gate) return _sequence;
        }
    }

    public DelegationIssuerTrust? Resolve(string issuer)
    {
        lock (_gate)
            return _trust.TryGetValue(issuer, out var value) ? value : null;
    }

    public void Set(DelegationIssuerTrust trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        if (string.IsNullOrWhiteSpace(trust.Issuer))
            throw new ArgumentException("Issuer is required.", nameof(trust));
        lock (_gate)
        {
            _trust[trust.Issuer] = trust;
            checked
            {
                _sequence++;
            }
        }
    }

    public void Remove(string issuer)
    {
        lock (_gate)
        {
            _trust.Remove(issuer);
            checked
            {
                _sequence++;
            }
        }
    }

    public DelegationIssuerTrustSnapshot Capture(string issuer, string keyId)
    {
        lock (_gate)
        {
            if (!_trust.TryGetValue(issuer, out var trust))
                throw new InvalidOperationException($"Delegation issuer '{issuer}' is not trusted.");
            if (!trust.AllowsKey(keyId))
                throw new InvalidOperationException("Delegation key is not trusted by the issuer.");

            var state = trust.GetKeyState(keyId);
            return new DelegationIssuerTrustSnapshot(
                issuer,
                _sequence,
                Fingerprint(trust),
                keyId,
                state);
        }
    }

    private static string Fingerprint(DelegationIssuerTrust trust)
    {
        var keys = trust.SigningKeyIds
            .OrderBy(x => x, StringComparer.Ordinal);

        var states = trust.KeyStates
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key + "=" + x.Value);

        var tenants = trust.AllowedTenants?
                          .OrderBy(x => x, StringComparer.Ordinal)
                      ?? Enumerable.Empty<string>();

        var canonical = string.Join("\n", [
            trust.Issuer,
            trust.CanDelegate ? "1" : "0",
            trust.Audience ?? "",
            string.Join("\u001f", keys),
            string.Join("\u001f", states),
            string.Join("\u001f", tenants)
        ]);

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public static class SecurityWarrantDelegationTrustTransition
{
    /// <summary>Captures the exact issuer trust state used to authorize a delegation.</summary>
    public static DelegationIssuerTrustSnapshot ValidateAndCapture(
        SecurityWarrant parent,
        SecurityWarrant child,
        ISecurityWarrantDelegationTrustStateResolver trust,
        DateTimeOffset now,
        string? tenant = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(trust);

        SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, trust, now, tenant);
        var snapshot = trust.Capture(child.Issuer, child.KeyId);
        if (snapshot.KeyState != DelegationIssuerKeyState.Active)
            throw new InvalidOperationException("Only an active issuer key may authorize a new delegation.");
        return snapshot;
    }

    /// <summary>Final execution gate: trust and key lifecycle must be unchanged since validation.</summary>
    public static void AssertUnchanged(
        DelegationIssuerTrustSnapshot snapshot,
        ISecurityWarrantDelegationTrustStateResolver trust)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.AssertCurrent(trust);
    }
}