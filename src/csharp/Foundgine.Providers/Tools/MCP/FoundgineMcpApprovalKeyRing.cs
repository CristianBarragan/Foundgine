using System.Security.Cryptography;

namespace Foundgine.Providers.Tools.MCP;

public enum FoundgineMcpApprovalKeyRole { Signing, VerifyOnly }

/// <summary>A currently usable key. <paramref name="VerifiableUntilUnixMilliseconds"/> is null when it has no deadline.</summary>
public sealed record FoundgineMcpApprovalKeyInfo(
    string KeyId, FoundgineMcpApprovalKeyRole Role, long? VerifiableUntilUnixMilliseconds);

/// <summary>
/// Rotating key set for <c>fgmcp1</c> approval tokens. It is both the signer (it signs with the single active signing key)
/// and the key resolver (it verifies with the active key and every retained public key that has not passed its deadline).
/// </summary>
/// <remarks>
/// <para><b>Rotation.</b> <see cref="ActivateSigningKey(string, RSA, TimeSpan)"/> installs a new signing key and demotes the
/// previous one to verify-only for the given retention, so outstanding approvals keep verifying. The retention must be at
/// least the configured minimum; size it with <see cref="FoundgineMcpApprovalTokenPolicy.MinimumKeyRetention"/>. Hosts that
/// only verify learn about rotations with <see cref="AddVerificationKey"/> and <see cref="RetireAfter"/>.</para>
/// <para><b>Rules.</b> A key id is never reused: it stays reserved after expiry or revocation. <see cref="Revoke"/> removes a
/// key immediately and is the response to a suspected compromise. Verification has no side effects, and unknown, expired and
/// revoked ids are all reported as "no key". RSA keys must be at least <see cref="MinimumRsaKeyBits"/> bits. Signing is RSA
/// PKCS#1 v1.5 / SHA-256, so existing tokens and the golden vector are unchanged.</para>
/// <para>The ring does not authenticate requests and does not replace <see cref="FoundgineMcpApprovalTokenPolicy"/>.
/// It keeps its own public-key copies, never disposes caller-supplied RSA objects, and holds only the active private key.
/// Thread-safe: readers use an immutable snapshot, mutations are serialized.</para>
/// </remarks>
public sealed class FoundgineMcpApprovalKeyRing : IFoundgineMcpApprovalTokenSigner, IFoundgineMcpApprovalTokenKeyResolver
{
    public const int MinimumRsaKeyBits = 2048;
    private const long NoDeadline = long.MaxValue;

    /// <summary>The private key is present only for the active signing key.</summary>
    private sealed record Entry(RSA PublicKey, RSA? PrivateKey, long VerifiableUntil);

    private sealed record State(string? SigningKeyId, Dictionary<string, Entry> Keys, HashSet<string> Revoked);

    private readonly TimeSpan _minimumRetention;
    private readonly Func<long> _clock;
    private readonly IFoundgineMcpAuditSink? _audit;
    private readonly object _gate = new();
    private volatile State _state = new(null, new Dictionary<string, Entry>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    /// <param name="minimumRetention">Shortest allowed retention for a superseded key; prefer <see cref="ForPolicy"/>.</param>
    /// <param name="unixMillisecondsClock">Test seam; defaults to the system clock.</param>
    /// <param name="auditSink">Receives events for activation, rotation, added verification keys, retirement and revocation,
    /// after each change has taken effect. Events never carry key material.</param>
    public FoundgineMcpApprovalKeyRing(
        TimeSpan? minimumRetention = null, Func<long>? unixMillisecondsClock = null, IFoundgineMcpAuditSink? auditSink = null)
    {
        _audit = auditSink;
        _minimumRetention = minimumRetention ?? TimeSpan.Zero;
        if (_minimumRetention < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumRetention));
        _clock = unixMillisecondsClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>A ring whose minimum retention is the policy's token age plus skew.</summary>
    public static FoundgineMcpApprovalKeyRing ForPolicy(FoundgineMcpApprovalTokenPolicy policy) =>
        new((policy ?? throw new ArgumentNullException(nameof(policy))).MinimumKeyRetention);

    private void Emit(FoundgineMcpAuditEventType type, string keyId, string? detail) =>
        FoundgineMcpAudit.Emit(_audit, new FoundgineMcpAuditEvent(type, _clock(), keyId, null, detail));

    // ---- signing ----------------------------------------------------------------------------------------------------

    /// <summary>The id of the active signing key. Throws <see cref="InvalidOperationException"/> when none is active.</summary>
    public string KeyId => _state.SigningKeyId ?? throw new InvalidOperationException("No active approval-token signing key.");

    /// <summary>Signs with the active key. <see cref="FoundgineMcpApprovalToken.Encode"/> uses <see cref="Snapshot"/> instead.</summary>
    public byte[] Sign(ReadOnlySpan<byte> payload) => Snapshot().Sign(payload);

    /// <summary>A signer fixed to the key that is active now, so id and signature in one token never come from different keys.</summary>
    public IFoundgineMcpApprovalTokenSigner Snapshot()
    {
        var s = _state;
        if (s.SigningKeyId is null) throw new InvalidOperationException("No active approval-token signing key.");
        return new FoundgineMcpRsaApprovalTokenSigner(s.SigningKeyId, s.Keys[s.SigningKeyId].PrivateKey!);
    }

    // ---- verification -----------------------------------------------------------------------------------------------

    /// <summary>The public key for <paramref name="keyId"/>, or null when unknown, revoked or past its deadline.</summary>
    public RSA? Resolve(string keyId)
    {
        if (keyId is null) return null;
        if (!_state.Keys.TryGetValue(keyId, out var e) || _clock() >= e.VerifiableUntil) return null;
        return e.PublicKey;
    }

    /// <summary>Usable keys right now: the signing key first, then verify-only keys by id.</summary>
    public IReadOnlyList<FoundgineMcpApprovalKeyInfo> Keys()
    {
        var s = _state;
        var now = _clock();
        return s.Keys
            .Where(kv => now < kv.Value.VerifiableUntil)
            .Select(kv => new FoundgineMcpApprovalKeyInfo(kv.Key,
                kv.Key == s.SigningKeyId ? FoundgineMcpApprovalKeyRole.Signing : FoundgineMcpApprovalKeyRole.VerifyOnly,
                kv.Value.VerifiableUntil == NoDeadline ? null : kv.Value.VerifiableUntil))
            .OrderBy(k => k.Role)
            .ThenBy(k => k.KeyId, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- rotation ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Installs the first signing key (or the first after <see cref="Revoke"/> removed the previous one). Use the overload
    /// with a retention to replace an active key.
    /// </summary>
    public void ActivateSigningKey(string keyId, RSA privateKey)
    {
        lock (_gate)
        {
            if (_state.SigningKeyId is not null)
                throw new InvalidOperationException(
                    "A signing key is already active; pass the retention for the previous key to rotate.");
            Install(keyId, privateKey, null);
            Emit(FoundgineMcpAuditEventType.KeyActivated, keyId, null);
        }
    }

    /// <summary>
    /// Rotates: <paramref name="keyId"/> becomes the signing key and the previous signing key stays verifiable for
    /// <paramref name="retainPreviousFor"/> (positive and at least the configured minimum). Nothing changes if validation fails.
    /// </summary>
    public void ActivateSigningKey(string keyId, RSA privateKey, TimeSpan retainPreviousFor)
    {
        lock (_gate)
        {
            if (_state.SigningKeyId is null)
            {
                Install(keyId, privateKey, null);
                Emit(FoundgineMcpAuditEventType.KeyActivated, keyId, null);
                return;
            }
            var previous = _state.SigningKeyId;
            Install(keyId, privateKey, RequireRetention(retainPreviousFor));
            Emit(FoundgineMcpAuditEventType.KeyRotated, keyId, $"previous={previous} retainedFor={retainPreviousFor}");
        }
    }

    /// <summary>Trusts a public key for verification only. The deadline may be null (none) or must lie at least the configured minimum retention in the future.</summary>
    public void AddVerificationKey(string keyId, RSA publicKey, DateTimeOffset? verifiableUntil = null)
    {
        RequireKeyId(keyId);
        lock (_gate)
        {
            var copy = PublicCopy(publicKey);
            var deadline = NoDeadline;
            if (verifiableUntil is { } until)
            {
                deadline = until.ToUnixTimeMilliseconds();
                var now = _clock();
                if (deadline <= now) throw new ArgumentException("The verification deadline must be in the future.", nameof(verifiableUntil));
                RequireRetention(TimeSpan.FromMilliseconds(Math.Max(1, deadline - now)));
            }
            var s = _state;
            RequireUnused(s, keyId);
            var keys = new Dictionary<string, Entry>(s.Keys, StringComparer.Ordinal) { [keyId] = new Entry(copy, null, deadline) };
            _state = new State(s.SigningKeyId, keys, s.Revoked);
            Emit(FoundgineMcpAuditEventType.KeyAdded, keyId, deadline == NoDeadline ? "verifiableUntil=none" : $"verifiableUntil={deadline}");
        }
    }

    /// <summary>
    /// Schedules a verify-only key to stop verifying after <paramref name="retainFor"/> (at least the configured minimum).
    /// It only ever shortens a deadline. The signing key cannot be retired: activate its replacement first.
    /// </summary>
    /// <returns>False when the key is unknown or already unusable.</returns>
    public bool RetireAfter(string keyId, TimeSpan retainFor)
    {
        RequireKeyId(keyId);
        lock (_gate)
        {
            var now = _clock();
            var deadline = SaturatingAdd(now, (long)RequireRetention(retainFor).TotalMilliseconds);
            var s = _state;
            if (keyId == s.SigningKeyId)
                throw new InvalidOperationException("The active signing key cannot be retired; activate a replacement first.");
            if (!s.Keys.TryGetValue(keyId, out var e) || now >= e.VerifiableUntil) return false;
            var effective = Math.Min(e.VerifiableUntil, deadline);
            var keys = new Dictionary<string, Entry>(s.Keys, StringComparer.Ordinal)
            { [keyId] = new Entry(e.PublicKey, null, effective) };
            _state = new State(s.SigningKeyId, keys, s.Revoked);
            Emit(FoundgineMcpAuditEventType.KeyRetired, keyId, $"verifiableUntil={effective}");
            return true;
        }
    }

    /// <summary>
    /// Removes a key immediately, whatever its retention, and reserves its id forever. Revoking the signing key leaves the
    /// ring unable to sign until a new key is activated. Unknown ids are reserved too.
    /// </summary>
    /// <returns>True if a key was removed.</returns>
    public bool Revoke(string keyId)
    {
        RequireKeyId(keyId);
        lock (_gate)
        {
            var s = _state;
            var keys = new Dictionary<string, Entry>(s.Keys, StringComparer.Ordinal);
            var removed = keys.Remove(keyId);
            var revoked = new HashSet<string>(s.Revoked, StringComparer.Ordinal) { keyId };
            var wasSigning = keyId == s.SigningKeyId;
            _state = new State(wasSigning ? null : s.SigningKeyId, keys, revoked);
            Emit(FoundgineMcpAuditEventType.KeyRevoked, keyId, $"removed={removed.ToString().ToLowerInvariant()} wasSigning={wasSigning.ToString().ToLowerInvariant()}");
            return removed;
        }
    }

    // ---- internals --------------------------------------------------------------------------------------------------

    private void Install(string keyId, RSA privateKey, TimeSpan? retainPrevious)
    {
        RequireKeyId(keyId);
        ArgumentNullException.ThrowIfNull(privateKey);
        var pub = PublicCopy(privateKey);
        // Prove the key can sign and that the public copy verifies it, before any state changes.
        try
        {
            var probe = new byte[] { 1, 2, 3 };
            var sig = privateKey.SignData(probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!pub.VerifyData(probe, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new ArgumentException("The key cannot produce verifiable signatures.", nameof(privateKey));
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException("A usable RSA private key is required.", nameof(privateKey), ex);
        }
        var s = _state;
        RequireUnused(s, keyId);
        var keys = new Dictionary<string, Entry>(s.Keys, StringComparer.Ordinal);
        if (retainPrevious is { } retain)
        {
            var old = keys[s.SigningKeyId!];
            keys[s.SigningKeyId!] = new Entry(old.PublicKey, null, SaturatingAdd(_clock(), (long)retain.TotalMilliseconds));
        }
        keys[keyId] = new Entry(pub, privateKey, NoDeadline);
        _state = new State(keyId, keys, s.Revoked);
    }

    private TimeSpan RequireRetention(TimeSpan retain)
    {
        if (retain <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retain), "Retention must be positive.");
        if (retain < _minimumRetention)
            throw new ArgumentOutOfRangeException(nameof(retain),
                $"Retention is shorter than the minimum of {_minimumRetention}; outstanding approvals signed by the old key would stop verifying early.");
        return retain;
    }

    private static void RequireUnused(State s, string keyId)
    {
        if (s.Keys.ContainsKey(keyId) || s.Revoked.Contains(keyId))
            throw new ArgumentException($"Approval-token key id '{keyId}' was already used; key ids are never reused.", nameof(keyId));
    }

    private static void RequireKeyId(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("A key id is required.", nameof(keyId));
    }

    private static RSA PublicCopy(RSA key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.KeySize < MinimumRsaKeyBits)
            throw new ArgumentException($"RSA keys must be at least {MinimumRsaKeyBits} bits.", nameof(key));
        var copy = RSA.Create();
        copy.ImportParameters(key.ExportParameters(false));
        return copy;
    }

    private static long SaturatingAdd(long a, long b)
    {
        try { return checked(a + b); }
        catch (OverflowException) { return long.MaxValue; }
    }
}
