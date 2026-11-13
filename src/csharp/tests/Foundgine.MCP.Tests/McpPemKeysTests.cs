using System.Security.Cryptography;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpPemKeysTests
{
    [Fact]
    public void RoundTripsPkcs8AndSpkiAndActivatesARing()
    {
        using var source = RSA.Create(2048);
        using var priv = FoundgineMcpPemKeys.PrivateKey(source.ExportPkcs8PrivateKeyPem());
        using var pub = FoundgineMcpPemKeys.PublicKey(source.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(source.ExportParameters(false).Modulus, pub.ExportParameters(false).Modulus);
        Assert.Equal(source.ExportParameters(false).Modulus, priv.ExportParameters(false).Modulus);
        // CRLF line endings and surrounding whitespace are fine.
        using var crlf = FoundgineMcpPemKeys.PrivateKey("\n  " + source.ExportPkcs8PrivateKeyPem().Replace("\n", "\r\n") + "\n");

        var ring = new FoundgineMcpApprovalKeyRing();
        ring.ActivateSigningKey("k1", priv);
        Assert.Equal("k1", ring.KeyId);
        Assert.NotNull(ring.Resolve("k1"));
    }

    [Fact]
    public void RefusesLegacyEncryptedWrongAndMultipleBlocksWithoutEchoingKeyText()
    {
        using var source = RSA.Create(2048);
        var der = source.ExportPkcs8PrivateKey();
        string Pem(string label) => PemEncoding.WriteString(label, der);
        var body = Convert.ToBase64String(der);

        var legacy = Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(Pem("RSA PRIVATE KEY")));
        Assert.Contains("openssl pkcs8", legacy.Message);
        Assert.DoesNotContain(body[..40], legacy.Message);
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(Pem("ENCRYPTED PRIVATE KEY")));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(Pem("PUBLIC KEY")));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(Pem("PRIVATE KEY") + Pem("PRIVATE KEY")));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(Pem("PRIVATE KEY") + "trailing"));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey("junk " + Pem("PRIVATE KEY")));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(null));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey("   "));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey("-----BEGIN PRIVATE KEY-----\n!!!notbase64!!!\n-----END PRIVATE KEY-----"));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PrivateKey(PemEncoding.WriteString("PRIVATE KEY", new byte[] { 1, 2, 3 })));
        Assert.Throws<ArgumentException>(() => FoundgineMcpPemKeys.PublicKey(new string('x', 70_000)));
    }
}
