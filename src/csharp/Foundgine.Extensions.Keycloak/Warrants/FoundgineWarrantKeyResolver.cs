using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.Keycloak.Configuration;

namespace Foundgine.Extensions.Keycloak.Warrants;

/// <summary>
/// Verification side of the warrant bridge: maps a warrant's key id to the RSA public key that must have signed it.
/// Unknown key ids resolve to <c>null</c>, which the verifier rejects ("Unknown warrant key"). Give it to the runtime with
/// <c>options.WarrantKeyResolver = sp.GetRequiredService&lt;ISecurityWarrantKeyResolver&gt;()</c> and
/// <c>options.ExpectedWarrantIssuer = keycloakOptions.WarrantIssuer</c>.
/// </summary>
public sealed class FoundgineWarrantKeyResolver : ISecurityWarrantKeyResolver, IDisposable
{
    private readonly Dictionary<string, RSA> _keys = new(StringComparer.Ordinal);

    public FoundgineWarrantKeyResolver(IEnumerable<KeyValuePair<string, string>> trustedPublicKeys)
    {
        foreach (var (keyId, pemOrPath) in trustedPublicKeys) Add(keyId, pemOrPath);
    }

    /// <summary>Trusts this host's own signing key (under its key id) plus <see cref="FoundgineKeycloakOptions.TrustedWarrantPublicKeys"/>.</summary>
    public static FoundgineWarrantKeyResolver Create(FoundgineKeycloakOptions options, IFoundgineWarrantSigningKeyProvider own)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(own);
        var trusted = new Dictionary<string, string>(options.TrustedWarrantPublicKeys, StringComparer.Ordinal)
        {
            [own.KeyId] = own.ExportPublicKeyPem()
        };
        return new FoundgineWarrantKeyResolver(trusted);
    }

    /// <summary>Key ids this resolver trusts (never key material).</summary>
    public IReadOnlyCollection<string> KeyIds => _keys.Keys;

    /// <summary>Accepts PEM text or a path to a PEM file. Private keys are rejected: a verifier must never hold one.</summary>
    private void Add(string keyId, string pemOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pemOrPath);
        var pem = pemOrPath.Contains("-----BEGIN", StringComparison.Ordinal) ? pemOrPath : File.ReadAllText(pemOrPath);
        if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new CryptographicException($"Trusted key '{keyId}' contains private key material; configure the public key only.");

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048) throw new CryptographicException($"Trusted key '{keyId}' must be at least 2048 bits.");
            if (!_keys.TryAdd(keyId, rsa)) throw new ArgumentException($"Duplicate trusted warrant key id '{keyId}'.");
        }
        catch { rsa.Dispose(); throw; }
    }

    // Public-key RSA instances are used concurrently by the verifier (checked with 2000 parallel verifications) and
    // are never disposed by it, so one shared instance per key id is safe.
    public RSA Resolve(string keyId) => keyId is not null && _keys.TryGetValue(keyId, out var rsa) ? rsa : null!;

    public void Dispose() { foreach (var k in _keys.Values) k.Dispose(); }
}
