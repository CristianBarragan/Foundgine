using System.Security.Cryptography;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// Loads RSA keys for the <see cref="FoundgineMcpApprovalKeyRing"/> from PEM text. Deliberately strict: exactly one block, and
/// only the two standard unencrypted labels, <c>PRIVATE KEY</c> (PKCS#8) and <c>PUBLIC KEY</c> (X.509 SubjectPublicKeyInfo).
/// Legacy <c>RSA PRIVATE KEY</c> (PKCS#1), encrypted keys and certificates are refused with a message that says how to convert
/// them. Reading the file and protecting it is the host's job; error messages never echo key text. The caller owns (and
/// disposes) the returned <see cref="RSA"/>; the key ring keeps its own copies.
/// </summary>
public static class FoundgineMcpPemKeys
{
    private const int MaxPemChars = 64 * 1024;

    public static RSA PrivateKey(string? pem)
    {
        var der = Der(pem, "PRIVATE KEY");
        var rsa = RSA.Create();
        try { rsa.ImportPkcs8PrivateKey(der, out _); return rsa; }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            rsa.Dispose();
            throw new ArgumentException("The PEM is not a valid RSA PKCS#8 private key.");
        }
    }

    public static RSA PublicKey(string? pem)
    {
        var der = Der(pem, "PUBLIC KEY");
        var rsa = RSA.Create();
        try { rsa.ImportSubjectPublicKeyInfo(der, out _); return rsa; }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            rsa.Dispose();
            throw new ArgumentException("The PEM is not a valid RSA public key.");
        }
    }

    private static byte[] Der(string? pem, string expectedLabel)
    {
        if (string.IsNullOrWhiteSpace(pem)) throw new ArgumentException("A PEM block is required.");
        if (pem.Length > MaxPemChars) throw new ArgumentException("The PEM is too large.");
        var text = pem.Replace("\r\n", "\n").Trim();
        const string begin = "-----BEGIN ";
        if (!text.StartsWith(begin, StringComparison.Ordinal))
            throw new ArgumentException($"Expected a PEM block starting with -----BEGIN {expectedLabel}-----.");
        var labelEnd = text.IndexOf("-----", begin.Length, StringComparison.Ordinal);
        if (labelEnd < 0) throw new ArgumentException("Malformed PEM header.");
        var label = text[begin.Length..labelEnd];
        if (label != expectedLabel)
        {
            var hint = label switch
            {
                "RSA PRIVATE KEY" => " Convert it with: openssl pkcs8 -topk8 -nocrypt -in key.pem",
                "ENCRYPTED PRIVATE KEY" => " Decrypt it first; the host must handle the passphrase.",
                "CERTIFICATE" => " Extract the key with: openssl x509 -pubkey -noout -in cert.pem",
                _ => ""
            };
            throw new ArgumentException($"Unsupported PEM label '{label}'; expected {expectedLabel}.{hint}");
        }
        var end = $"-----END {expectedLabel}-----";
        var endAt = text.IndexOf(end, StringComparison.Ordinal);
        if (endAt < 0) throw new ArgumentException("Missing PEM footer.");
        if (text[(endAt + end.Length)..].Trim().Length != 0)
            throw new ArgumentException("Exactly one PEM block is allowed; trailing content was found.");
        var body = text[(labelEnd + 5)..endAt];
        if (body.Contains("-----", StringComparison.Ordinal)) throw new ArgumentException("Exactly one PEM block is allowed.");
        try { return Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c)))); }
        catch (FormatException) { throw new ArgumentException("The PEM body is not valid base64."); }
    }
}
