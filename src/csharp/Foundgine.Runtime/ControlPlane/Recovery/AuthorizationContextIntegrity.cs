using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Runtime.ControlPlane;

/// <summary>
/// External key material used to authenticate persisted authorization context.
/// Key bytes must never be persisted in PostgreSQL.
/// </summary>
public sealed class AuthorizationContextIntegrityKey
{
    public AuthorizationContextIntegrityKey(string keyId, byte[] key)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Integrity key id is required.", nameof(keyId));
        if (key is null || key.Length < 32)
            throw new ArgumentException("Integrity keys must contain at least 32 bytes.", nameof(key));

        KeyId = keyId;
        Key = key.ToArray();
    }

    public string KeyId { get; }
    public byte[] Key { get; }
}

/// <summary>
/// Key ring for authorization-context integrity. The active key is used for new
/// writes; all configured keys may verify existing records during rotation.
/// </summary>
public enum AuthorizationIntegrityKeyState
{
    Active,
    VerificationOnly,
    Retired
}

public sealed record AuthorizationIntegrityKeyDescriptor(
    string KeyId,
    AuthorizationIntegrityKeyState State);

public sealed record AuthorizationKeyRotationProvenance(
    Guid OperatorId,
    long RotationSequence,
    string CredentialFingerprint);

/// <summary>
/// Immutable verification/signing key-ring snapshot.
///
/// makes lifecycle state explicit: exactly one key is active for new
/// evidence, verification-only keys may authenticate existing evidence, and
/// retired keys are unavailable. The key material never leaves process memory.
/// </summary>
public sealed class AuthorizationContextIntegrityKeyRing
{
    private const string AlgorithmVersion = "HMAC-SHA256/v1";
    private readonly IReadOnlyDictionary<string, byte[]> _keys;
    private readonly IReadOnlyDictionary<string, AuthorizationIntegrityKeyState> _states;

    public AuthorizationContextIntegrityKeyRing(
        AuthorizationContextIntegrityKey activeKey,
        IEnumerable<AuthorizationContextIntegrityKey>? verificationKeys = null,
        long configurationVersion = 1,
        long lastRotationSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(activeKey);
        if (configurationVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationVersion));
        if (lastRotationSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(lastRotationSequence));

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [activeKey.KeyId] = activeKey.Key.ToArray()
        };
        var states = new Dictionary<string, AuthorizationIntegrityKeyState>(StringComparer.Ordinal)
        {
            [activeKey.KeyId] = AuthorizationIntegrityKeyState.Active
        };

        if (verificationKeys is not null)
        {
            foreach (var key in verificationKeys)
            {
                ArgumentNullException.ThrowIfNull(key);
                if (string.Equals(key.KeyId, activeKey.KeyId, StringComparison.Ordinal))
                {
                    if (!CryptographicOperations.FixedTimeEquals(keys[key.KeyId], key.Key))
                        throw new InvalidOperationException(
                            $"Integrity key id '{key.KeyId}' is configured with different key material.");
                    continue;
                }

                if (keys.TryGetValue(key.KeyId, out var existing) &&
                    !CryptographicOperations.FixedTimeEquals(existing, key.Key))
                    throw new InvalidOperationException(
                        $"Integrity key id '{key.KeyId}' is configured with different key material.");

                keys[key.KeyId] = key.Key.ToArray();
                states[key.KeyId] = AuthorizationIntegrityKeyState.VerificationOnly;
            }
        }

        _keys = keys;
        _states = states;
        ActiveKeyId = activeKey.KeyId;
        ConfigurationVersion = configurationVersion;
        LastRotationSequence = lastRotationSequence;
    }

    private AuthorizationContextIntegrityKeyRing(
        string activeKeyId,
        Dictionary<string, byte[]> keys,
        Dictionary<string, AuthorizationIntegrityKeyState> states,
        long configurationVersion,
        long lastRotationSequence)
    {
        _keys = keys.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.Ordinal);
        _states = new Dictionary<string, AuthorizationIntegrityKeyState>(states, StringComparer.Ordinal);
        ActiveKeyId = activeKeyId;
        ConfigurationVersion = configurationVersion;
        LastRotationSequence = lastRotationSequence;
    }

    public string ActiveKeyId { get; }
    public long ConfigurationVersion { get; }
    public long LastRotationSequence { get; }

    public static string CurrentAlgorithmVersion => AlgorithmVersion;

    public IReadOnlyList<AuthorizationIntegrityKeyDescriptor> DescribeKeys() =>
        _states.Select(k => new AuthorizationIntegrityKeyDescriptor(k.Key, k.Value))
            .OrderBy(k => k.KeyId, StringComparer.Ordinal)
            .ToArray();

    public AuthorizationIntegrityKeyState GetState(string keyId) =>
        _states.TryGetValue(keyId, out var state)
            ? state
            : throw new KeyNotFoundException($"Integrity key id '{keyId}' is not configured.");

    public bool CanVerify(string keyId) =>
        _states.TryGetValue(keyId, out var state) && state != AuthorizationIntegrityKeyState.Retired;

    /// <summary>
    /// Creates the next immutable ring snapshot. The previous active key becomes
    /// verification-only. Rotation requires a strictly increasing provenance
    /// sequence, preventing concurrent/replayed rotation commands.
    /// </summary>
    public AuthorizationContextIntegrityKeyRing Rotate(
        AuthorizationContextIntegrityKey newActiveKey,
        AuthorizationKeyRotationProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(newActiveKey);
        ValidateRotationProvenance(provenance);

        if (provenance.RotationSequence <= LastRotationSequence)
            throw new InvalidOperationException(
                $"Key rotation sequence is stale. Last={LastRotationSequence}, requested={provenance.RotationSequence}.");

        if (_states.TryGetValue(newActiveKey.KeyId, out var existingState))
        {
            if (existingState == AuthorizationIntegrityKeyState.Retired)
                throw new InvalidOperationException(
                    $"Retired integrity key '{newActiveKey.KeyId}' cannot be reactivated.");
            if (!_keys.TryGetValue(newActiveKey.KeyId, out var existing) ||
                !CryptographicOperations.FixedTimeEquals(existing, newActiveKey.Key))
                throw new InvalidOperationException(
                    $"Integrity key id '{newActiveKey.KeyId}' is configured with different key material.");
        }

        var keys = _keys.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.Ordinal);
        var states = new Dictionary<string, AuthorizationIntegrityKeyState>(_states, StringComparer.Ordinal);
        if (!keys.ContainsKey(newActiveKey.KeyId))
            keys[newActiveKey.KeyId] = newActiveKey.Key.ToArray();

        states[ActiveKeyId] = AuthorizationIntegrityKeyState.VerificationOnly;
        states[newActiveKey.KeyId] = AuthorizationIntegrityKeyState.Active;

        return new AuthorizationContextIntegrityKeyRing(
            newActiveKey.KeyId, keys, states, ConfigurationVersion + 1, provenance.RotationSequence);
    }

    /// <summary>
    /// Retires a non-active key only after the caller supplies the set of key ids
    /// still referenced by persisted evidence. This makes premature retirement a
    /// hard failure rather than an availability accident.
    /// </summary>
    public AuthorizationContextIntegrityKeyRing Retire(
        string keyId,
        AuthorizationKeyRotationProvenance provenance,
        IReadOnlySet<string> persistedKeyIds)
    {
        ValidateRotationProvenance(provenance);
        ArgumentNullException.ThrowIfNull(persistedKeyIds);

        if (provenance.RotationSequence <= LastRotationSequence)
            throw new InvalidOperationException(
                $"Key lifecycle sequence is stale. Last={LastRotationSequence}, requested={provenance.RotationSequence}.");
        if (!_states.TryGetValue(keyId, out var state))
            throw new KeyNotFoundException($"Integrity key id '{keyId}' is not configured.");
        if (state == AuthorizationIntegrityKeyState.Active ||
            string.Equals(keyId, ActiveKeyId, StringComparison.Ordinal))
            throw new InvalidOperationException("The active integrity key cannot be retired.");
        if (persistedKeyIds.Contains(keyId))
            throw new InvalidOperationException(
                $"Integrity key '{keyId}' is still referenced by persisted authorization evidence.");

        var keys = _keys.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.Ordinal);
        var states = new Dictionary<string, AuthorizationIntegrityKeyState>(_states, StringComparer.Ordinal)
        {
            [keyId] = AuthorizationIntegrityKeyState.Retired
        };

        return new AuthorizationContextIntegrityKeyRing(
            ActiveKeyId, keys, states, ConfigurationVersion + 1, provenance.RotationSequence);
    }

    private void ValidateRotationProvenance(AuthorizationKeyRotationProvenance provenance)
    {
        if (provenance.OperatorId == Guid.Empty)
            throw new UnauthorizedAccessException("Key rotation requires an identified operator.");
        if (provenance.RotationSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(provenance), "Key rotation sequence must be positive.");
        if (string.IsNullOrWhiteSpace(provenance.CredentialFingerprint))
            throw new UnauthorizedAccessException("Key rotation credential provenance is required.");
    }

    public string ComputeContextTag(Guid actorId, int tenantId, bool allowed, long version, string fingerprint)
        => ComputeTag("context", actorId, tenantId, allowed, version, fingerprint, ActiveKeyId);

    public bool VerifyContextTag(Guid actorId, int tenantId, bool allowed, long version, string fingerprint,
        string algorithmVersion, string keyId, string tag)
        => Verify("context", actorId, tenantId, allowed, version, fingerprint, algorithmVersion, keyId, tag);

    public string ComputeTombstoneTag(Guid actorId, int tenantId, long version, string fingerprint)
        => ComputeTag("tombstone", actorId, tenantId, false, version, fingerprint, ActiveKeyId);

    public bool VerifyTombstoneTag(Guid actorId, int tenantId, long version, string fingerprint,
        string algorithmVersion, string keyId, string tag)
        => Verify("tombstone", actorId, tenantId, false, version, fingerprint, algorithmVersion, keyId, tag);

    private string ComputeTag(string recordType, Guid actorId, int tenantId, bool allowed, long version,
        string fingerprint, string keyId)
    {
        if (!_states.TryGetValue(keyId, out var state) || state != AuthorizationIntegrityKeyState.Active)
            throw new InvalidOperationException(
                $"Integrity key '{keyId}' is not active for new authorization evidence.");

        var key = _keys[keyId];
        var payload = Canonicalize(recordType, actorId, tenantId, allowed, version, fingerprint, AlgorithmVersion,
            keyId);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    private bool Verify(string recordType, Guid actorId, int tenantId, bool allowed, long version,
        string fingerprint, string algorithmVersion, string keyId, string tag)
    {
        if (!string.Equals(algorithmVersion, AlgorithmVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(tag) ||
            !CanVerify(keyId) || !_keys.TryGetValue(keyId, out var key))
            return false;

        var payload = Canonicalize(recordType, actorId, tenantId, allowed, version, fingerprint, algorithmVersion,
            keyId);
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        try
        {
            var supplied = Convert.FromHexString(tag);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Canonicalize(string recordType, Guid actorId, int tenantId, bool allowed,
        long version, string fingerprint, string algorithmVersion, string keyId)
    {
        if (string.IsNullOrWhiteSpace(recordType) || string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("Integrity payload contains a required empty field.");

        // Length-prefixing avoids delimiter ambiguity and makes the representation
        // independent of locale, JSON ordering, or serializer behavior.
        return string.Join("|",
            Encode(recordType),
            Encode(algorithmVersion),
            Encode(keyId),
            Encode(actorId.ToString("D")),
            Encode(tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Encode(allowed ? "1" : "0"),
            Encode(version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Encode(fingerprint));
    }

    private static string Encode(string value) => $"{value.Length}:{value}";
}

/// <summary>
/// Process-local atomic owner of the current key-ring snapshot. A rotation
/// publishes a complete immutable snapshot under one lock, so readers never
/// observe a partially rotated configuration.
/// </summary>
public interface IAuthorizationKeyRotationAuthorizer
{
    bool IsAuthorized(AuthorizationKeyRotationProvenance provenance);
}

/// <summary>
/// External key-lifecycle authority. Operator credentials are represented only
/// by their fingerprints; credential material is never persisted in PostgreSQL.
/// </summary>
public sealed class AuthorizationKeyRotationAuthorizer : IAuthorizationKeyRotationAuthorizer
{
    private readonly IReadOnlyDictionary<Guid, string> _operators;

    public AuthorizationKeyRotationAuthorizer(IReadOnlyDictionary<Guid, string> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);
        if (operators.Count == 0)
            throw new ArgumentException("At least one authorized key-rotation operator is required.",
                nameof(operators));

        _operators = new Dictionary<Guid, string>(operators);
    }

    public bool IsAuthorized(AuthorizationKeyRotationProvenance provenance) =>
        provenance.OperatorId != Guid.Empty &&
        _operators.TryGetValue(provenance.OperatorId, out var expected) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provenance.CredentialFingerprint));
}

public sealed class RejectAllAuthorizationKeyRotationAuthorizer : IAuthorizationKeyRotationAuthorizer
{
    public bool IsAuthorized(AuthorizationKeyRotationProvenance provenance) => false;
}

public sealed class AuthorizationContextIntegrityKeyRingManager
{
    private readonly object _gate = new();
    private readonly IAuthorizationKeyRotationAuthorizer _authorizer;
    private AuthorizationContextIntegrityKeyRing _current;

    public AuthorizationContextIntegrityKeyRingManager(
        AuthorizationContextIntegrityKeyRing initialRing,
        IAuthorizationKeyRotationAuthorizer authorizer)
    {
        _current = initialRing ?? throw new ArgumentNullException(nameof(initialRing));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    public AuthorizationContextIntegrityKeyRingManager(AuthorizationContextIntegrityKeyRing initialRing)
        : this(initialRing, new RejectAllAuthorizationKeyRotationAuthorizer())
    {
    }

    public AuthorizationContextIntegrityKeyRing Snapshot
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public AuthorizationContextIntegrityKeyRing Rotate(
        AuthorizationContextIntegrityKey newActiveKey,
        AuthorizationKeyRotationProvenance provenance)
    {
        lock (_gate)
        {
            if (!_authorizer.IsAuthorized(provenance))
                throw new UnauthorizedAccessException("Key rotation provenance is not authorized.");
            _current = _current.Rotate(newActiveKey, provenance);
            return _current;
        }
    }

    public AuthorizationContextIntegrityKeyRing Retire(
        string keyId,
        AuthorizationKeyRotationProvenance provenance,
        IReadOnlySet<string> persistedKeyIds)
    {
        lock (_gate)
        {
            if (!_authorizer.IsAuthorized(provenance))
                throw new UnauthorizedAccessException("Key lifecycle provenance is not authorized.");
            _current = _current.Retire(keyId, provenance, persistedKeyIds);
            return _current;
        }
    }
}