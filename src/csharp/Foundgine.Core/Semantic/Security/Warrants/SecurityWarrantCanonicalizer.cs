using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Canonical, provider-neutral representation used for signing and hashing.
/// Canonical form v1, shared byte for byte with the Java implementation (see src/conformance/README.md):
/// no whitespace, fixed property order, minimal JSON escaping (only <c>"</c>, <c>\</c> and C0 controls; everything
/// else, including non-ASCII and <c>+ &amp; &lt; &gt; ' `</c>, is emitted raw), UTF-16 code-unit ordering, UTC
/// timestamps with seven fractional digits and a literal <c>Z</c>, decimals with their scale preserved.
/// Do not use a general-purpose JSON serializer here: framework defaults differ between runtimes.
/// </summary>
public static class SecurityWarrantCanonicalizer
{
    public static byte[] UnsignedBytes(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        return new UTF8Encoding(false, true).GetBytes(UnsignedJson(warrant));
    }

    public static string UnsignedJson(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        var grants = warrant.Grants
            .Select(x => (x.Capability, x.Operation, Scopes: Normalize(x.ResourceScopes)))
            .OrderBy(x => x.Capability, StringComparer.Ordinal)
            .ThenBy(x => x.Operation, StringComparer.Ordinal)
            .ThenBy(x => string.Join("\u001f", x.Scopes), StringComparer.Ordinal)
            .ToList();

        var s = new StringBuilder("{");
        Key(s, "id", true); Str(s, warrant.Id);
        Key(s, "issuer", false); Str(s, warrant.Issuer);
        Key(s, "subject", false); Str(s, warrant.Subject);
        Key(s, "audience", false); Str(s, warrant.Audience);
        Key(s, "grants", false);
        s.Append('[');
        for (var i = 0; i < grants.Count; i++)
        {
            if (i > 0) s.Append(',');
            var g = grants[i];
            s.Append('{');
            Key(s, "capability", true); Str(s, g.Capability);
            Key(s, "operation", false); Str(s, g.Operation);
            Key(s, "resourceScopes", false); WriteArray(s, g.Scopes);
            s.Append('}');
        }

        s.Append(']');
        var c = warrant.Constraints;
        Key(s, "constraints", false);
        s.Append('{');
        Key(s, "allowedTenants", true); WriteArray(s, Normalize(c.AllowedTenants));
        Key(s, "allowedFields", false); WriteArray(s, Normalize(c.AllowedFields));
        Key(s, "resourceScopes", false); WriteArray(s, Normalize(c.ResourceScopes));
        Key(s, "allowedOperations", false); WriteArray(s, Normalize(c.AllowedOperations));
        Key(s, "maxResults", false);
        s.Append(c.MaxResults is { } mr ? mr.ToString(CultureInfo.InvariantCulture) : "null");
        Key(s, "maxAmount", false);
        s.Append(c.MaxAmount is { } ma ? ma.ToString(CultureInfo.InvariantCulture) : "null");
        s.Append('}');
        Key(s, "issuedAt", false); Str(s, Format(warrant.IssuedAt));
        Key(s, "expiresAt", false); Str(s, Format(warrant.ExpiresAt));
        Key(s, "nonce", false); Str(s, warrant.Nonce);
        Key(s, "keyId", false); Str(s, warrant.KeyId);
        Key(s, "parentId", false); OptStr(s, warrant.ParentId);
        Key(s, "parentDigest", false); OptStr(s, warrant.ParentDigest);
        Key(s, "delegationPath", false); WriteArray(s, warrant.DelegationPath);
        s.Append('}');
        return s.ToString();
    }

    public static string Digest(SecurityWarrant warrant) =>
        Convert.ToHexString(SHA256.HashData(UnsignedBytes(warrant)));

    // Mirrors the Java record constructors, which always normalize: drop blanks, de-duplicate, sort by code unit.
    private static List<string> Normalize(IEnumerable<string>? values) =>
        (values ?? []).Where(x => !WarrantText.IsBlank(x)).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static void Key(StringBuilder s, string key, bool first)
    {
        if (!first) s.Append(',');
        Str(s, key);
        s.Append(':');
    }

    private static void WriteArray(StringBuilder s, IEnumerable<string> values)
    {
        s.Append('[');
        var first = true;
        foreach (var v in values)
        {
            if (!first) s.Append(',');
            first = false;
            Str(s, v);
        }

        s.Append(']');
    }

    private static void OptStr(StringBuilder s, string? v)
    {
        if (v is null) s.Append("null");
        else Str(s, v);
    }

    private static void Str(StringBuilder s, string? v)
    {
        ArgumentNullException.ThrowIfNull(v, "canonical string");
        s.Append('"');
        for (var i = 0; i < v.Length; i++)
        {
            var c = v[i];
            switch (c)
            {
                case '"': s.Append("\\\""); break;
                case '\\': s.Append("\\\\"); break;
                case '\n': s.Append("\\n"); break;
                case '\r': s.Append("\\r"); break;
                case '\t': s.Append("\\t"); break;
                case '\b': s.Append("\\b"); break;
                case '\f': s.Append("\\f"); break;
                default:
                    if (c < 0x20) s.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 >= v.Length || !char.IsLowSurrogate(v[i + 1]))
                            throw new ArgumentException("Unpaired surrogate in canonical string.");
                        s.Append(c).Append(v[++i]);
                    }
                    else if (char.IsLowSurrogate(c))
                        throw new ArgumentException("Unpaired surrogate in canonical string.");
                    else s.Append(c);
                    break;
            }
        }

        s.Append('"');
    }

    private static string Format(DateTimeOffset value)
    {
        var u = value.UtcDateTime;
        return u.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}
