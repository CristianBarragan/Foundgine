package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.interfaces.RSAPublicKey;
import java.util.Base64;
import org.junit.jupiter.api.Test;

class McpPemKeysTest {
    private static String pem(String label, byte[] der) {
        return "-----BEGIN " + label + "-----\n" + Base64.getMimeEncoder(64, "\n".getBytes()).encodeToString(der) + "\n-----END " + label + "-----\n";
    }

    private static java.security.KeyPair pair() throws Exception {
        var g = KeyPairGenerator.getInstance("RSA");
        g.initialize(2048);
        return g.generateKeyPair();
    }

    @Test void roundTripsPkcs8AndSpkiAndDerivesThePublicHalf() throws Exception {
        var kp = pair();
        PrivateKey priv = FoundgineMcpPemKeys.privateKey(pem("PRIVATE KEY", kp.getPrivate().getEncoded()));
        var pub = (RSAPublicKey) FoundgineMcpPemKeys.publicKey(pem("PUBLIC KEY", kp.getPublic().getEncoded()));
        assertEquals(((RSAPublicKey) kp.getPublic()).getModulus(), pub.getModulus());
        assertEquals(pub.getModulus(), ((RSAPublicKey) FoundgineMcpPemKeys.publicKeyOf(priv)).getModulus());
        // CRLF line endings and surrounding whitespace are fine.
        assertNotNull(FoundgineMcpPemKeys.privateKey("\n  " + pem("PRIVATE KEY", kp.getPrivate().getEncoded()).replace("\n", "\r\n") + "\n"));
    }

    @Test void loadedKeysActivateARingEndToEnd() throws Exception {
        var kp = pair();
        var ring = new FoundgineMcpApprovalKeyRing();
        PrivateKey priv = FoundgineMcpPemKeys.privateKey(pem("PRIVATE KEY", kp.getPrivate().getEncoded()));
        ring.activateSigningKey("k1", priv, FoundgineMcpPemKeys.publicKeyOf(priv));
        assertEquals("k1", ring.keyId());
        assertNotNull(ring.resolve("k1"));
    }

    @Test void refusesLegacyEncryptedWrongAndMultipleBlocksWithoutEchoingKeyText() throws Exception {
        var der = pair().getPrivate().getEncoded();
        String body = Base64.getEncoder().encodeToString(der);
        var legacy = assertThrows(IllegalArgumentException.class,
                () -> FoundgineMcpPemKeys.privateKey(pem("RSA PRIVATE KEY", der)));
        assertTrue(legacy.getMessage().contains("openssl pkcs8"));
        assertFalse(legacy.getMessage().contains(body.substring(0, 40)));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey(pem("ENCRYPTED PRIVATE KEY", der)));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey(pem("PUBLIC KEY", der)));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey(pem("PRIVATE KEY", der) + pem("PRIVATE KEY", der)));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey(pem("PRIVATE KEY", der) + "trailing"));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey("junk " + pem("PRIVATE KEY", der)));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey(null));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey("   "));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey("-----BEGIN PRIVATE KEY-----\n!!!notbase64!!!\n-----END PRIVATE KEY-----"));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.privateKey(pem("PRIVATE KEY", new byte[] {1, 2, 3})));
        assertThrows(IllegalArgumentException.class, () -> FoundgineMcpPemKeys.publicKey("x".repeat(70_000)));
    }
}
