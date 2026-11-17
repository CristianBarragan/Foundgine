using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.Keycloak.Configuration;

namespace Foundgine.Extensions.Keycloak.Warrants;

/// <summary>Source of the RSA key that signs issued warrants. Back this with a KMS/HSM in production.</summary>
public interface IFoundgineWarrantSigningKeyProvider
{
    string KeyId { get; }
    RSA PrivateKey { get; }
    /// <summary>Public key PEM, to configure the runtime's <c>ISecurityWarrantKeyResolver</c>.</summary>
    string ExportPublicKeyPem();
}

public sealed class PemWarrantSigningKeyProvider : IFoundgineWarrantSigningKeyProvider, IDisposable
{
    private readonly RSA _key;
    public string KeyId { get; }
    public RSA PrivateKey => _key;

    public PemWarrantSigningKeyProvider(string keyId, string pem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);
        KeyId = keyId;
        _key = RSA.Create();
        _key.ImportFromPem(pem);
        if (_key.KeySize < 2048) throw new CryptographicException("Warrant signing keys must be at least 2048 bits.");
    }

    /// <summary>Development only: a new key per process. Warrants will not verify across instances or restarts.</summary>
    public static PemWarrantSigningKeyProvider CreateEphemeral(string keyId)
    {
        using var rsa = RSA.Create(2048);
        return new PemWarrantSigningKeyProvider(keyId, rsa.ExportPkcs8PrivateKeyPem());
    }

    public string ExportPublicKeyPem() => _key.ExportSubjectPublicKeyInfoPem();
    public void Dispose() => _key.Dispose();
}

/// <summary>Turns an already-authenticated identity (subject, tenant, roles) into a signed, short-lived Foundgine context.</summary>
public interface IFoundgineWarrantIssuer
{
    /// <returns>Null when the roles grant nothing: the caller has no Foundgine authority.</returns>
    SecurityExecutionContext? Issue(string subject, string tenant, IReadOnlyCollection<string> roles, TimeSpan? lifetime = null);
}

public sealed class FoundgineKeycloakWarrantIssuer(
    FoundgineKeycloakOptions options, IFoundgineWarrantSigningKeyProvider keys, TimeProvider? time = null)
    : IFoundgineWarrantIssuer
{
    private readonly object _signGate = new(); // RSA instances are not guaranteed thread-safe for concurrent use.
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public SecurityExecutionContext? Issue(
        string subject, string tenant, IReadOnlyCollection<string> roles, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);

        var grants = new List<CapabilityGrant>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (!options.RoleGrants.TryGetValue(role, out var mapped)) continue;
            foreach (var g in mapped)
            {
                var scopes = g.ResourceScopes.ToArray();
                if (seen.Add($"{g.Capability}|{g.Operation}|{string.Join(',', scopes.Order(StringComparer.Ordinal))}"))
                    grants.Add(new CapabilityGrant(g.Capability, g.Operation, scopes));
            }
        }
        if (grants.Count == 0) return null;

        var constraints = options.ConstraintsFactory?.Invoke(tenant, roles)
            ?? new SecurityWarrantConstraints([tenant], [], [], [], null, null);

        var now = _time.GetUtcNow();
        var unsigned = new SecurityWarrant(
            Id: Guid.NewGuid().ToString("N"),
            Issuer: options.WarrantIssuer,
            Subject: subject,
            Audience: options.WarrantAudience,
            Grants: grants,
            Constraints: constraints,
            IssuedAt: now,
            ExpiresAt: now + (lifetime ?? options.WarrantLifetime),
            Nonce: Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            KeyId: keys.KeyId,
            ParentId: null!, // root warrant
            Signature: []);

        SecurityWarrant signed;
        lock (_signGate) signed = SecurityWarrantSigner.Sign(unsigned, keys.PrivateKey);

        var scope = options.ResourceScopeResolver?.Invoke(tenant, roles) ?? tenant;
        return new SecurityExecutionContext(signed, subject, options.WarrantAudience, tenant, scope);
    }
}
