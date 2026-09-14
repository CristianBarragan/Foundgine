using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Canonical, provider-neutral representation used for signing and hashing.</summary>
public static class SecurityWarrantCanonicalizer
{
    public static byte[] UnsignedBytes(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        return Encoding.UTF8.GetBytes(UnsignedJson(warrant));
    }

    public static string UnsignedJson(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        var grants = warrant.Grants
            .OrderBy(x => x.Capability, StringComparer.Ordinal)
            .ThenBy(x => x.Operation, StringComparer.Ordinal)
            .ThenBy(x => string.Join("\u001f", x.ResourceScopes), StringComparer.Ordinal)
            .Select(x => new
            {
                capability = x.Capability, operation = x.Operation,
                resourceScopes = x.ResourceScopes.OrderBy(v => v, StringComparer.Ordinal).ToArray()
            })
            .ToArray();

        var payload = new
        {
            id = warrant.Id,
            issuer = warrant.Issuer,
            subject = warrant.Subject,
            audience = warrant.Audience,
            grants,
            constraints = new
            {
                allowedTenants = warrant.Constraints.AllowedTenants,
                allowedFields = warrant.Constraints.AllowedFields,
                resourceScopes = warrant.Constraints.ResourceScopes,
                allowedOperations = warrant.Constraints.AllowedOperations,
                maxResults = warrant.Constraints.MaxResults,
                maxAmount = warrant.Constraints.MaxAmount
            },
            issuedAt = warrant.IssuedAt.ToUniversalTime().ToString("O"),
            expiresAt = warrant.ExpiresAt.ToUniversalTime().ToString("O"),
            nonce = warrant.Nonce,
            keyId = warrant.KeyId,
            parentId = warrant.ParentId,
            parentDigest = warrant.ParentDigest,
            delegationPath = warrant.DelegationPath
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    public static string Digest(SecurityWarrant warrant) =>
        Convert.ToHexString(SHA256.HashData(UnsignedBytes(warrant)));
}