using Foundgine.Providers.Gateway.Approvals;
using Xunit;

namespace Foundgine.EventStore.Tests;

/// <summary>DB-free tests for the AES-256-GCM approval envelope; mirrors Java ApprovalPayloadEnvelopeTest.</summary>
public sealed class ApprovalPayloadEnvelopeTests
{
    // Produced by the Java implementation with key bytes 7..38 and nonce bytes 0..11.
    private const string Vector = "{\"v\":1,\"kid\":\"vec-1\",\"n\":\"AAECAwQFBgcICQoL\",\"c\":\"WB4POqmwxqUIbQzVCLv/QmgRFb0cT6SGYNzsTf+L+tU=\"}";

    private static byte[] Key(int seed) => Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray();

    private sealed class Keys : IFoundgineGatewayApprovalKeyProvider
    {
        public readonly Dictionary<string, byte[]> Store = new()
        {
            ["k1"] = Key(1), ["k2"] = Key(50), ["vec-1"] = Key(7)
        };
        public string CurrentKeyId => "k1";
        public byte[] GetKey(string keyId) => Store.TryGetValue(keyId, out var key)
            ? key : throw new InvalidOperationException("unknown or retired key " + keyId);
    }

    [Fact]
    public void RoundTripHidesPlaintextAndUsesAFreshNonce()
    {
        var keys = new Keys();
        var envelope = ApprovalPayloadEnvelope.Seal("{\"subject\":\"alice\"}", "k1", Key(1));
        Assert.DoesNotContain("alice", envelope);
        Assert.Equal("{\"subject\":\"alice\"}", ApprovalPayloadEnvelope.Open(envelope, keys));
        Assert.NotEqual(envelope, ApprovalPayloadEnvelope.Seal("{\"subject\":\"alice\"}", "k1", Key(1)));
    }

    [Fact]
    public void TamperedCiphertextAndRelabelledKeyIdFailClosed()
    {
        var keys = new Keys();
        var envelope = ApprovalPayloadEnvelope.Seal("secret", "k1", Key(1));
        var at = envelope.IndexOf("\"c\":\"", StringComparison.Ordinal) + 5;
        var tampered = envelope[..at] + (envelope[at] == 'A' ? 'B' : 'A') + envelope[(at + 1)..];
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(tampered, keys));
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(
            envelope.Replace("\"kid\":\"k1\"", "\"kid\":\"k2\""), keys));
    }

    [Fact]
    public void RetiredKeyFailsClosed()
    {
        var keys = new Keys();
        var envelope = ApprovalPayloadEnvelope.Seal("secret", "k1", Key(1));
        Assert.Equal("secret", ApprovalPayloadEnvelope.Open(envelope, keys));
        keys.Store.Remove("k1");
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(envelope, keys));
    }

    [Fact]
    public void JsonbWhitespaceAndPropertyReorderingAreAcceptedButDuplicateOrExtraPropertiesAreRejected()
    {
        var keys = new Keys();
        var envelope = ApprovalPayloadEnvelope.Seal("payload", "k1", Key(1));
        var fields = envelope[1..^1].Split(',');
        var reordered = "{ " + string.Join(", ", fields.Reverse()) + " }";
        Assert.Equal("payload", ApprovalPayloadEnvelope.Open(reordered, keys));
        Assert.Equal("payload", ApprovalPayloadEnvelope.Open(envelope + "  ", keys));
        var extra = envelope[..^1] + ",\"extra\":true}";
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(extra, keys));
        var duplicate = envelope[..^1] + ",\"v\":1}";
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(duplicate, keys));
        Assert.Throws<InvalidOperationException>(() => ApprovalPayloadEnvelope.Open(envelope + " true", keys));
    }

    [Fact]
    public void CrossRuntimeVectorDecryptsAndReproduces()
    {
        var keys = new Keys();
        Assert.Equal("foundgine-vector", ApprovalPayloadEnvelope.Open(Vector, keys));
        var nonce = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        Assert.Equal(Vector, ApprovalPayloadEnvelope.Seal("foundgine-vector", "vec-1", Key(7), nonce));
    }
}
