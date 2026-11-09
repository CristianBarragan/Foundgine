using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Semantics.Tests.Security.Warrants;

/// <summary>
/// Signature interoperability: RSA PKCS#1 v1.5 / SHA-256 is deterministic, so signing a vector warrant must reproduce
/// the independently generated signature byte for byte, and verification must accept or reject each case with the
/// same message as Java. Vectors: src/conformance/warrant-signatures.json (test-only keys). Set
/// FOUNDGINE_CONFORMANCE_DIR to override the directory.
/// </summary>
public sealed class WarrantSignatureConformanceTests
{
    public static IEnumerable<object[]> Names()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Behaves_like_the_reference_case(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        var c = doc.RootElement.GetProperty("cases").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == name);
        var k1 = doc.RootElement.GetProperty("keys").GetProperty("k-1");
        using var priv = RSA.Create();
        priv.ImportPkcs8PrivateKey(Convert.FromBase64String(k1.GetProperty("privateKeyPkcs8").GetString()!), out _);
        using var pub = RSA.Create();
        pub.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k1.GetProperty("publicKeySpki").GetString()!), out _);

        var signature = Convert.FromBase64String(c.GetProperty("signature").GetString()!);
        var warrant = WarrantConformanceFactory.Build(c.GetProperty("warrant")) with { Signature = signature };
        var resolvable = c.GetProperty("resolvableKeyIds").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
        var resolver = new Resolver(id => resolvable.Contains(id) ? pub : null);

        string? error = null;
        try
        {
            SecurityWarrantVerifier.Verify(warrant, resolver,
                DateTimeOffset.Parse(c.GetProperty("now").GetString()!, CultureInfo.InvariantCulture),
                Text(c, "expectedIssuer"), Text(c, "expectedAudience"));
        }
        catch (InvalidOperationException e)
        {
            error = e.Message;
        }

        Assert.Equal(Text(c, "error"), error);
        if (c.GetProperty("mustReproduceSignature").GetBoolean())
            Assert.Equal(c.GetProperty("signature").GetString(),
                Convert.ToBase64String(SecurityWarrantSigner.Sign(warrant with { Signature = [] }, priv).Signature));
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    private static string VectorFile()
    {
        var overrideDir = Environment.GetEnvironmentVariable("FOUNDGINE_CONFORMANCE_DIR");
        if (overrideDir is not null) return Path.Combine(overrideDir, "warrant-signatures.json");
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "conformance", "warrant-signatures.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Conformance vectors not found; set FOUNDGINE_CONFORMANCE_DIR.");
    }

    private sealed class Resolver(Func<string, RSA?> find) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) => find(keyId)!;
    }
}

/// <summary>Builds a warrant from a conformance-vector JSON object; shared by the conformance tests.</summary>
internal static class WarrantConformanceFactory
{
    public static SecurityWarrant Build(JsonElement w)
    {
        var grants = w.GetProperty("grants")
            .EnumerateArray()
            .Select(g => new CapabilityGrant(
                g.GetProperty("capability").GetString()!,
                g.GetProperty("operation").GetString()!,
                (IEnumerable<string>)StringsOrEmpty(g, "resourceScopes")))
            .ToList();

        var c = w.GetProperty("constraints");

        long? maxResults =
            c.TryGetProperty("maxResults", out var mr) &&
            mr.ValueKind == JsonValueKind.Number
                ? mr.GetInt64()
                : null;

        decimal? maxAmount =
            Text(c, "maxAmount") is { } ma
                ? decimal.Parse(ma, CultureInfo.InvariantCulture)
                : null;

        var constraints = new SecurityWarrantConstraints(
            (IEnumerable<string>)StringsOrEmpty(c, "allowedTenants"),
            (IEnumerable<string>)StringsOrEmpty(c, "allowedFields"),
            (IEnumerable<string>)StringsOrEmpty(c, "resourceScopes"),
            (IEnumerable<string>)StringsOrEmpty(c, "allowedOperations"),
            maxResults,
            maxAmount);

        return new SecurityWarrant(
            w.GetProperty("id").GetString()!,
            w.GetProperty("issuer").GetString()!,
            w.GetProperty("subject").GetString()!,
            w.GetProperty("audience").GetString()!,
            grants,
            constraints,
            DateTimeOffset.Parse(
                w.GetProperty("issuedAt").GetString()!,
                CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(
                w.GetProperty("expiresAt").GetString()!,
                CultureInfo.InvariantCulture),
            w.GetProperty("nonce").GetString()!,
            w.GetProperty("keyId").GetString()!,
            Text(w, "parentId"),
            [])
        {
            ParentDigest = Text(w, "parentDigest"),
            DelegationPath = StringsOrEmpty(w, "delegationPath")
        };
    }

    public static List<string> Strings(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
                .Select(x => x.GetString()!)
                .ToList()
            : [];

    public static List<string> StringsOrEmpty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value)
            ? Strings(value)
            : [];

    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
}
