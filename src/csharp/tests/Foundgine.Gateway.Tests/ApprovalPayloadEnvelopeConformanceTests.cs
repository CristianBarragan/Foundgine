using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Foundgine.Providers.Gateway.Approvals;
using Xunit;

namespace Foundgine.Gateway.Tests;

/// <summary>
/// Cross-language conformance for the approval payload envelope: asserts the same vectors
/// (src/conformance/approval-envelope-vectors.json) as the Java test of the same name. Set FOUNDGINE_CONFORMANCE_DIR to
/// override the vector directory.
/// </summary>
public sealed class ApprovalPayloadEnvelopeConformanceTests
{
    public static IEnumerable<object[]> SealNames() => Names("sealCases");
    public static IEnumerable<object[]> AcceptNames() => Names("acceptCases");
    public static IEnumerable<object[]> RejectNames() => Names("rejectCases");

    [Theory]
    [MemberData(nameof(SealNames))]
    public void Seal_reproduces_reference_envelope_and_opens_back(string name)
    {
        using var doc = Load();
        var keys = new VectorKeys(doc.RootElement);
        var c = Case(doc, "sealCases", name);
        var keyId = c.GetProperty("keyId").GetString()!;
        var nonce = Convert.FromHexString(c.GetProperty("nonceHex").GetString()!);
        var plaintext = c.GetProperty("plaintext").GetString()!;
        var envelope = c.GetProperty("envelope").GetString()!;

        Assert.Equal(envelope, ApprovalPayloadEnvelope.Seal(plaintext, keyId, keys.GetKey(keyId), nonce));
        Assert.Equal(plaintext, ApprovalPayloadEnvelope.Open(envelope, keys));
    }

    [Theory]
    [MemberData(nameof(AcceptNames))]
    public void Open_accepts_postgres_jsonb_canonicalization(string name)
    {
        using var doc = Load();
        var keys = new VectorKeys(doc.RootElement);
        var c = Case(doc, "acceptCases", name);
        Assert.Equal(c.GetProperty("plaintext").GetString(),
            ApprovalPayloadEnvelope.Open(c.GetProperty("envelope").GetString()!, keys));
    }

    [Theory]
    [MemberData(nameof(RejectNames))]
    public void Open_fails_closed(string name)
    {
        using var doc = Load();
        var keys = new VectorKeys(doc.RootElement);
        var envelope = Case(doc, "rejectCases", name).GetProperty("envelope").GetString()!;
        Assert.ThrowsAny<Exception>(() => ApprovalPayloadEnvelope.Open(envelope, keys));
    }

    [Fact]
    public void Random_nonce_produces_distinct_envelopes_for_the_same_plaintext()
    {
        using var doc = Load();
        var keys = new VectorKeys(doc.RootElement);
        var a = ApprovalPayloadEnvelope.Seal("{}", "k1", keys.GetKey("k1"));
        var b = ApprovalPayloadEnvelope.Seal("{}", "k1", keys.GetKey("k1"));
        Assert.NotEqual(a, b);
        Assert.Equal("{}", ApprovalPayloadEnvelope.Open(a, keys));
    }

    [Fact]
    public void Rotation_old_key_still_opens_and_retired_key_fails_closed()
    {
        using var doc = Load();
        var keys = new VectorKeys(doc.RootElement); // current = k1, k2 still known
        var old = ApprovalPayloadEnvelope.Seal("{\"a\":1}", "k2", keys.GetKey("k2"));
        Assert.Equal("{\"a\":1}", ApprovalPayloadEnvelope.Open(old, keys));
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(old.Replace("\"k2\"", "\"k9\""), keys));
    }

    [Fact]
    public void Invalid_key_material_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Seal("{}", "k1", new byte[16], new byte[12]));
        Assert.Throws<ArgumentException>(() => ApprovalPayloadEnvelope.Seal("{}", "bad id", new byte[32], new byte[12]));
        Assert.Throws<ArgumentException>(() => ApprovalPayloadEnvelope.Seal("{}", "k1", new byte[32], new byte[8]));
        Assert.Throws<ArgumentException>(() => ApprovalPayloadEnvelope.Seal("{}", "k1\n", new byte[32], new byte[12]));
    }

    private sealed class VectorKeys(JsonElement root) : IFoundgineGatewayApprovalKeyProvider
    {
        public string CurrentKeyId => root.GetProperty("currentKeyId").GetString()!;

        public byte[] GetKey(string keyId) =>
            root.GetProperty("keys").TryGetProperty(keyId, out var hex)
                ? Convert.FromHexString(hex.GetString()!)
                : throw new InvalidOperationException($"Unknown or retired approval key: {keyId}");
    }

    private static IEnumerable<object[]> Names(string array)
    {
        using var doc = Load();
        foreach (var c in doc.RootElement.GetProperty(array).EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    private static JsonElement Case(JsonDocument doc, string array, string name) =>
        doc.RootElement.GetProperty(array).EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllBytes(VectorFile()));

    private static string VectorFile()
    {
        var overrideDir = Environment.GetEnvironmentVariable("FOUNDGINE_CONFORMANCE_DIR");
        if (overrideDir is not null) return Path.Combine(overrideDir, "approval-envelope-vectors.json");
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "conformance", "approval-envelope-vectors.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Conformance vectors not found; set FOUNDGINE_CONFORMANCE_DIR.");
    }
}
