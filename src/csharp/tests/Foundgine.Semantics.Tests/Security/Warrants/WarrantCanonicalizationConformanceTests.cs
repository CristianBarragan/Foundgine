using System.Globalization;
using System.Text.Json;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Semantics.Tests.Security.Warrants;

/// <summary>
/// Cross-language conformance: asserts the same vectors (src/conformance/warrant-canonicalization.json) as the Java
/// test of the same name. A warrant signed by one implementation must verify in the other, so the canonical bytes and
/// digest must be identical. Set FOUNDGINE_CONFORMANCE_DIR to override the vector directory.
/// </summary>
public sealed class WarrantCanonicalizationConformanceTests
{
    public static IEnumerable<object[]> Names()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        foreach (var v in doc.RootElement.GetProperty("vectors").EnumerateArray())
            yield return [v.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Canonical_json_and_digest_match_the_reference_vectors(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        var vector = doc.RootElement.GetProperty("vectors").EnumerateArray()
            .Single(v => v.GetProperty("name").GetString() == name);
        var warrant = Build(vector.GetProperty("warrant"));

        Assert.Equal(vector.GetProperty("unsignedJson").GetString(), SecurityWarrantCanonicalizer.UnsignedJson(warrant));
        Assert.Equal(vector.GetProperty("digest").GetString(), SecurityWarrantCanonicalizer.Digest(warrant));
    }

    [Fact]
    public void Unpaired_surrogates_are_rejected_rather_than_silently_replaced()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        var first = Build(doc.RootElement.GetProperty("vectors")[0].GetProperty("warrant"));
        Assert.Throws<ArgumentException>(() => SecurityWarrantCanonicalizer.UnsignedJson(first with { Subject = "bad\uD800" }));
    }

    private static string VectorFile()
    {
        var overrideDir = Environment.GetEnvironmentVariable("FOUNDGINE_CONFORMANCE_DIR");
        if (overrideDir is not null) return Path.Combine(overrideDir, "warrant-canonicalization.json");
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "conformance", "warrant-canonicalization.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Conformance vectors not found; set FOUNDGINE_CONFORMANCE_DIR.");
    }

    

    private static List<string> Strings(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array
            ? e.EnumerateArray().Select(x => x.GetString()!).ToList()
            : [];

    private static List<string> StringsOrEmpty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? Strings(value)
            : [];

    private static string? Text(JsonElement w, string name) =>
        w.TryGetProperty(name, out var e) && e.ValueKind != JsonValueKind.Null ? e.GetString() : null;

    private static SecurityWarrant Build(JsonElement w)
    {
        var grants = w.GetProperty("grants").EnumerateArray().Select(g =>
            new CapabilityGrant(
                g.GetProperty("capability").GetString()!,
                g.GetProperty("operation").GetString()!,
                (IEnumerable<string>)StringsOrEmpty(g, "resourceScopes")
            )).ToList();

        var c = w.TryGetProperty("constraints", out var constraintsElement)
            ? constraintsElement
            : default;

        long? maxResults =
            c.ValueKind == JsonValueKind.Object &&
            c.TryGetProperty("maxResults", out var mr) &&
            mr.ValueKind == JsonValueKind.Number
                ? mr.GetInt64()
                : null;

        decimal? maxAmount =
            c.ValueKind == JsonValueKind.Object &&
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
}
