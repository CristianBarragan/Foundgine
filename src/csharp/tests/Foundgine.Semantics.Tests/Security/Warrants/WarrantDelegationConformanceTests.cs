using System.Globalization;
using System.Text.Json;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Semantics.Tests.Security.Warrants;

/// <summary>
/// Cross-language conformance for delegation: attenuation and chain validation must accept and reject exactly the same
/// scenarios, with the same exception message, as the Java test of the same name. Vectors:
/// src/conformance/delegation-scenarios.json. Set FOUNDGINE_CONFORMANCE_DIR to override the directory.
/// </summary>
public sealed class WarrantDelegationConformanceTests
{
    public static IEnumerable<object[]> Names()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ScenarioFile()));
        foreach (var s in doc.RootElement.GetProperty("scenarios").EnumerateArray())
            yield return [s.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Behaves_like_the_reference_scenario(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ScenarioFile()));
        var scenario = doc.RootElement.GetProperty("scenarios").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == name);
        var warrants = scenario.GetProperty("warrants").EnumerateArray().Select(Build).ToList();
        var digests = Strings(scenario.GetProperty("digests"));
        for (var i = 0; i < warrants.Count; i++) Assert.Equal(digests[i], warrants[i].Digest);

        var now = DateTimeOffset.Parse(scenario.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
        string? error = null;
        try
        {
            if (scenario.GetProperty("op").GetString() == "attenuate")
                SecurityWarrantAttenuator.Attenuate(warrants[0], warrants[1], now);
            else
                SecurityWarrantDelegationChainValidator.Validate(warrants, now, Text(scenario, "expectedRootDigest"));
        }
        catch (InvalidOperationException e)
        {
            error = e.Message;
        }

        Assert.Equal(Text(scenario, "error"), error);
        if (Text(scenario, "chainDigest") is { } expected)
            Assert.Equal(expected, SecurityWarrantDelegationChainValidator.ChainDigest(warrants));
    }

    private static string ScenarioFile()
    {
        var overrideDir = Environment.GetEnvironmentVariable("FOUNDGINE_CONFORMANCE_DIR");
        if (overrideDir is not null) return Path.Combine(overrideDir, "delegation-scenarios.json");
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "conformance", "delegation-scenarios.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Conformance scenarios not found; set FOUNDGINE_CONFORMANCE_DIR.");
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
}
