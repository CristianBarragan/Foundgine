using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>
/// AES-256-GCM envelope shared byte-for-byte with Java. The envelope is a JSON object persisted in
/// PostgreSQL jsonb; parsing therefore accepts insignificant whitespace and property reordering while
/// rejecting duplicate, missing, or additional properties. AAD is
/// <c>foundgine.approval.v1|&lt;kid&gt;</c>.
/// </summary>
internal static class ApprovalPayloadEnvelope
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Seal(string plaintext, string keyId, byte[] key)
    {
        Span<byte> nonce = stackalloc byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);
        return Seal(plaintext, keyId, key, nonce.ToArray());
    }

    /// <summary>Fixed-nonce overload for cross-runtime test vectors only; never reuse a nonce with a key.</summary>
    internal static string Seal(string plaintext, string keyId, byte[] key, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        RequireKeyId(keyId);
        RequireKey(key);
        if (nonce is null || nonce.Length != NonceBytes)
            throw new ArgumentException("nonce must be 12 bytes", nameof(nonce));

        var clear = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[clear.Length];
        var tag = new byte[TagBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, clear, ciphertext, tag, Aad(keyId));
        }
        catch (CryptographicException e)
        {
            throw new InvalidOperationException("Approval payload encryption failed.", e);
        }

        var combined = new byte[ciphertext.Length + TagBytes];
        ciphertext.CopyTo(combined, 0);
        tag.CopyTo(combined, ciphertext.Length);
        return "{\"v\":1,\"kid\":\"" + keyId + "\",\"n\":\"" + Convert.ToBase64String(nonce)
            + "\",\"c\":\"" + Convert.ToBase64String(combined) + "\"}";
    }

    internal static string Open(string envelope, IFoundgineGatewayApprovalKeyProvider keys)
    {
        if (envelope is null || keys is null)
            throw new ArgumentException("envelope and keys are required");

        try
        {
            using var document = JsonDocument.Parse(envelope, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Approval payload is not a JSON object.");

            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name))
                    throw new InvalidOperationException("Approval payload contains a duplicate property.");
            }
            if (fields.Count != 4 || !fields.SetEquals(new[] { "v", "kid", "n", "c" }))
                throw new InvalidOperationException("Approval payload has missing or unexpected properties.");

            if (!root.GetProperty("v").TryGetInt32(out var version) || version != 1)
                throw new InvalidOperationException("Unsupported approval payload envelope version.");
            var keyId = root.GetProperty("kid").GetString();
            RequireKeyId(keyId);
            var nonceText = root.GetProperty("n").GetString();
            var ciphertextText = root.GetProperty("c").GetString();
            if (nonceText is null || ciphertextText is null)
                throw new InvalidOperationException("Approval payload has invalid field types.");

            var nonce = Convert.FromBase64String(nonceText);
            var combined = Convert.FromBase64String(ciphertextText);
            // Require canonical Base64 so multiple encodings cannot represent the same envelope bytes.
            if (!string.Equals(Convert.ToBase64String(nonce), nonceText, StringComparison.Ordinal)
                || !string.Equals(Convert.ToBase64String(combined), ciphertextText, StringComparison.Ordinal)
                || nonce.Length != NonceBytes || combined.Length < TagBytes)
                throw new InvalidOperationException("Approval payload has invalid nonce or ciphertext.");

            var key = keys.GetKey(keyId!);
            RequireKey(key);
            var cipherLength = combined.Length - TagBytes;
            var clear = new byte[cipherLength];
            using (var aes = new AesGcm(key, TagBytes))
                aes.Decrypt(nonce, combined.AsSpan(0, cipherLength), combined.AsSpan(cipherLength, TagBytes), clear, Aad(keyId!));
            return StrictUtf8.GetString(clear);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception e) when (e is JsonException or FormatException or CryptographicException
            or ArgumentException or DecoderFallbackException)
        {
            throw new InvalidOperationException("Approval payload failed authentication or has an invalid envelope.", e);
        }
    }

    private static byte[] Aad(string keyId) => Encoding.UTF8.GetBytes("foundgine.approval.v1|" + keyId);

    internal static void RequireKeyId(string? keyId)
    {
        if (string.IsNullOrEmpty(keyId) || keyId.Length > 64
            || keyId.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new ArgumentException("Key id must match [A-Za-z0-9._-]{1,64}.", nameof(keyId));
    }

    private static void RequireKey(byte[]? key)
    {
        if (key is null || key.Length != 32)
            throw new InvalidOperationException("Approval payload keys must be 32 bytes (AES-256).");
    }
}
