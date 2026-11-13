package com.foundgine.providers.tools.mcp;

import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.interfaces.RSAPrivateCrtKey;
import java.security.spec.PKCS8EncodedKeySpec;
import java.security.spec.RSAPublicKeySpec;
import java.security.spec.X509EncodedKeySpec;
import java.util.Base64;

/**
 * Loads RSA keys for the {@link FoundgineMcpApprovalKeyRing} from PEM text. Deliberately strict: exactly one block, and only
 * the two standard unencrypted labels, {@code PRIVATE KEY} (PKCS#8) and {@code PUBLIC KEY} (X.509 SubjectPublicKeyInfo).
 * Legacy {@code RSA PRIVATE KEY} (PKCS#1), encrypted keys and certificates are refused with a message that says how to convert
 * them, rather than guessed at. Reading the file and protecting it is the host's job; error messages never echo key text.
 */
public final class FoundgineMcpPemKeys {
    private static final int MAX_PEM_CHARS = 64 * 1024;

    private FoundgineMcpPemKeys() {}

    public static PrivateKey privateKey(String pem) {
        try {
            return KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(der(pem, "PRIVATE KEY")));
        } catch (GeneralSecurityException e) {
            throw new IllegalArgumentException("The PEM is not a valid RSA PKCS#8 private key.");
        }
    }

    public static PublicKey publicKey(String pem) {
        try {
            return KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(der(pem, "PUBLIC KEY")));
        } catch (GeneralSecurityException e) {
            throw new IllegalArgumentException("The PEM is not a valid RSA public key.");
        }
    }

    /**
     * The public half of an RSA private key that carries its CRT parameters (every PKCS#8 RSA key does in practice), so a
     * signing key can be activated from a single private PEM.
     */
    public static PublicKey publicKeyOf(PrivateKey privateKey) {
        if (!(privateKey instanceof RSAPrivateCrtKey crt))
            throw new IllegalArgumentException("The private key does not carry its public exponent; supply the public key separately.");
        try {
            return KeyFactory.getInstance("RSA").generatePublic(new RSAPublicKeySpec(crt.getModulus(), crt.getPublicExponent()));
        } catch (GeneralSecurityException e) {
            throw new IllegalArgumentException("Unable to derive the public key.");
        }
    }

    private static byte[] der(String pem, String expectedLabel) {
        if (pem == null || pem.isBlank()) throw new IllegalArgumentException("A PEM block is required.");
        if (pem.length() > MAX_PEM_CHARS) throw new IllegalArgumentException("The PEM is too large.");
        String text = pem.replace("\r\n", "\n").strip();
        int begin = text.indexOf("-----BEGIN ");
        if (begin != 0) throw new IllegalArgumentException("Expected a PEM block starting with -----BEGIN " + expectedLabel + "-----.");
        int labelEnd = text.indexOf("-----", 11);
        if (labelEnd < 0) throw new IllegalArgumentException("Malformed PEM header.");
        String label = text.substring(11, labelEnd);
        if (!label.equals(expectedLabel)) {
            String hint = switch (label) {
                case "RSA PRIVATE KEY" -> " Convert it with: openssl pkcs8 -topk8 -nocrypt -in key.pem";
                case "ENCRYPTED PRIVATE KEY" -> " Decrypt it first; the host must handle the passphrase.";
                case "CERTIFICATE" -> " Extract the key with: openssl x509 -pubkey -noout -in cert.pem";
                default -> "";
            };
            throw new IllegalArgumentException("Unsupported PEM label '" + label + "'; expected " + expectedLabel + "." + hint);
        }
        String end = "-----END " + expectedLabel + "-----";
        int endAt = text.indexOf(end);
        if (endAt < 0) throw new IllegalArgumentException("Missing PEM footer.");
        if (!text.substring(endAt + end.length()).isBlank())
            throw new IllegalArgumentException("Exactly one PEM block is allowed; trailing content was found.");
        String body = text.substring(labelEnd + 5, endAt);
        if (body.contains("-----")) throw new IllegalArgumentException("Exactly one PEM block is allowed.");
        try {
            return Base64.getMimeDecoder().decode(body.strip());
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("The PEM body is not valid base64.");
        }
    }
}
