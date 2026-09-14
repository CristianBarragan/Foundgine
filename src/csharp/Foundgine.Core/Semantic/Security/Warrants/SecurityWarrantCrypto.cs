using System.Security.Cryptography;

namespace Foundgine.Core.Semantic.Security.Warrants;

public interface ISecurityWarrantKeyResolver
{
    RSA Resolve(string keyId);
}

/// <summary>Creates RSA-SHA256 signatures over the canonical warrant representation.</summary>
public static class SecurityWarrantSigner
{
    public static SecurityWarrant Sign(SecurityWarrant warrant, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        ArgumentNullException.ThrowIfNull(privateKey);
        try
        {
            _ = privateKey.ExportParameters(true).D;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("The signing key must contain private key material.", ex);
        }

        var signature = privateKey.SignData(
            SecurityWarrantCanonicalizer.UnsignedBytes(warrant with { Signature = [] }),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return warrant with { Signature = signature };
    }
}

/// <summary>Verifies signature, issuer key, time bounds and warrant structure.</summary>
public static class SecurityWarrantVerifier
{
    public static void Verify(
        SecurityWarrant warrant,
        ISecurityWarrantKeyResolver keys,
        DateTimeOffset now,
        string? expectedIssuer = null,
        string? expectedAudience = null)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        ArgumentNullException.ThrowIfNull(keys);
        if (warrant.Signature.Length == 0)
            throw new InvalidOperationException("Security warrant has no signature.");
        if (warrant.ExpiresAt <= warrant.IssuedAt)
            throw new InvalidOperationException("Security warrant expiry must be after issued-at.");
        if (!warrant.IsTimeValid(now))
            throw new InvalidOperationException("Security warrant is expired or not yet valid.");
        // Fail closed: an unconfigured expected issuer is a host misconfiguration,
        // not an implicit "trust any issuer" decision. Callers that genuinely want
        // to skip issuer trust must do so explicitly, not by leaving this null.
        if (expectedIssuer is null)
            throw new InvalidOperationException("Security warrant verification requires a configured expected issuer.");
        if (!StringComparer.Ordinal.Equals(warrant.Issuer, expectedIssuer))
            throw new InvalidOperationException("Security warrant issuer is not trusted.");
        if (expectedAudience is not null && !StringComparer.Ordinal.Equals(warrant.Audience, expectedAudience))
            throw new InvalidOperationException("Security warrant audience is not trusted.");

        var key = keys.Resolve(warrant.KeyId) ??
                  throw new InvalidOperationException($"Unknown warrant key '{warrant.KeyId}'.");
        var valid = key.VerifyData(
            SecurityWarrantCanonicalizer.UnsignedBytes(warrant with { Signature = [] }),
            warrant.Signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        if (!valid)
            throw new InvalidOperationException("Security warrant signature is invalid.");
    }
}