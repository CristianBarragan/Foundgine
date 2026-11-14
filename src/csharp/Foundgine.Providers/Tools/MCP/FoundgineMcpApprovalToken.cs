using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>Host-owned private-key signing service for portable MCP approval tokens.</summary>
public interface IFoundgineMcpApprovalTokenSigner
{
    string KeyId { get; }
    byte[] Sign(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Returns a signer pinned to one key for a single <see cref="FoundgineMcpApprovalToken.Encode"/> call. Encode reads
    /// <see cref="KeyId"/> to build the payload and then calls <see cref="Sign"/>; a signer whose active key can change
    /// (such as <see cref="FoundgineMcpApprovalKeyRing"/> during rotation) must return a snapshot so both steps use the same
    /// key. Fixed-key signers keep this default.
    /// </summary>
    IFoundgineMcpApprovalTokenSigner Snapshot() => this;
}

/// <summary>RSA PKCS#1 v1.5 / SHA-256 signer for a single fixed key.</summary>
public sealed class FoundgineMcpRsaApprovalTokenSigner(string keyId, RSA privateKey) : IFoundgineMcpApprovalTokenSigner
{
    public string KeyId { get; } = string.IsNullOrWhiteSpace(keyId)
        ? throw new ArgumentException("A signing key id is required.", nameof(keyId)) : keyId;
    private readonly RSA _privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
    public byte[] Sign(ReadOnlySpan<byte> payload) =>
        _privateKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
}

/// <summary>Resolves a trusted public key by the token's key identifier.</summary>
public interface IFoundgineMcpApprovalTokenKeyResolver
{
    RSA? Resolve(string keyId);
}

/// <summary>Decoded, authenticated version-1 approval-token claims.</summary>
public sealed record FoundgineMcpApprovalTokenClaims(
    string KeyId,
    string ApprovalId,
    string PlanFingerprint,
    string ApprovedBy,
    long ApprovedAtUnixMilliseconds,
    string RequestJson);

/// <summary>
/// Cross-language token codec. The signed payload is a newline-delimited UTF-8 record with
/// base64url-encoded text fields, avoiding serializer-specific JSON canonicalization.
/// Wire form: fgmcp1.BASE64URL(payload).BASE64URL(RSA-SHA256-PKCS1(signature)).
/// </summary>
public static class FoundgineMcpApprovalToken
{
    private const string Prefix = "fgmcp1";

    public static string Encode(
        string approvalId,
        string planFingerprint,
        string approvedBy,
        DateTimeOffset approvedAt,
        string requestJson,
        IFoundgineMcpApprovalTokenSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        signer = signer.Snapshot() ?? throw new ArgumentException("Signer snapshot is required.", nameof(signer));
        Require(approvalId, nameof(approvalId));
        Require(planFingerprint, nameof(planFingerprint));
        Require(approvedBy, nameof(approvedBy));
        Require(requestJson, nameof(requestJson));
        Require(signer.KeyId, nameof(signer.KeyId));
        var payload = BuildPayload(signer.KeyId, approvalId, planFingerprint, approvedBy,
            approvedAt.ToUnixTimeMilliseconds(), requestJson);
        var signature = signer.Sign(payload);
        if (signature is not { Length: > 0 }) throw new CryptographicException("Approval token signer returned an empty signature.");
        return $"{Prefix}.{B64(payload)}.{B64(signature)}";
    }

    public static FoundgineMcpApprovalTokenClaims DecodeAndVerify(
        string token, IFoundgineMcpApprovalTokenKeyResolver keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var parts = token?.Split('.') ?? [];
        if (parts.Length != 3 || parts[0] != Prefix)
            throw new CryptographicException("Approval token format or version is invalid.");
        byte[] payload;
        byte[] signature;
        try { payload = FromB64(parts[1]); signature = FromB64(parts[2]); }
        catch (FormatException ex) { throw new CryptographicException("Approval token encoding is invalid.", ex); }
        string[] fields;
        try { fields = new UTF8Encoding(false, true).GetString(payload).Split('\n'); }
        catch (DecoderFallbackException ex) { throw new CryptographicException("Approval token payload is not valid UTF-8.", ex); }
        if (fields.Length != 7 || fields[0] != "1")
            throw new CryptographicException("Approval token payload is invalid.");
        string keyId;
        try { keyId = FromB64Text(fields[1]); }
        catch (FormatException ex) { throw new CryptographicException("Approval token claims are invalid.", ex); }
        var key = keys.Resolve(keyId) ?? throw new CryptographicException($"Unknown approval-token key '{keyId}'.");
        if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Approval token signature is invalid.");
        try
        {
            var approvalId = FromB64Text(fields[2]);
            var fingerprint = FromB64Text(fields[3]);
            var approvedBy = FromB64Text(fields[4]);
            if (!long.TryParse(fields[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var approvedAt))
                throw new FormatException();
            var request = FromB64Text(fields[6]);
            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(approvalId) ||
                string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(approvedBy) ||
                string.IsNullOrWhiteSpace(request))
                throw new FormatException();
            return new FoundgineMcpApprovalTokenClaims(keyId, approvalId, fingerprint, approvedBy, approvedAt, request);
        }
        catch (FormatException ex) { throw new CryptographicException("Approval token claims are invalid.", ex); }
    }

    private static byte[] BuildPayload(string keyId, string approvalId, string fingerprint,
        string approvedBy, long approvedAt, string requestJson) => Encoding.UTF8.GetBytes(string.Join('\n',
        "1", B64Text(keyId), B64Text(approvalId), B64Text(fingerprint), B64Text(approvedBy),
        approvedAt.ToString(CultureInfo.InvariantCulture), B64Text(requestJson)));

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    private static string B64Text(string value) => B64(Encoding.UTF8.GetBytes(value));
    private static string FromB64Text(string value) => new UTF8Encoding(false, true).GetString(FromB64(value));
    private static void Require(string? value, string name)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Approval token field is required.", name); }
}
