package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import java.security.*;
import java.util.Map;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Base64;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.security.spec.X509EncodedKeySpec;
import java.security.spec.PKCS8EncodedKeySpec;
import org.junit.jupiter.api.Test;

class McpApprovalTokenInteropTest {
    @Test void verifiesSharedGoldenVectorProducedOutsideJava() throws Exception {
        JsonNode vector = new ObjectMapper().readTree(Files.readString(
                Path.of("../../conformance/mcp-approval-token-golden.json")));
        String pem = vector.path("publicKeyPem").asText();
        String encoded = pem.replace("-----BEGIN PUBLIC KEY-----", "")
                .replace("-----END PUBLIC KEY-----", "").replaceAll("\\s", "");
        PublicKey publicKey = KeyFactory.getInstance("RSA").generatePublic(
                new X509EncodedKeySpec(Base64.getDecoder().decode(encoded)));
        var claims = FoundgineMcpApprovalToken.decodeAndVerify(
                vector.path("token").asText(), id -> id.equals("golden-key-1") ? publicKey : null);
        JsonNode expected = vector.path("claims");
        assertEquals(expected.path("keyId").asText(), claims.keyId());
        assertEquals(expected.path("approvalId").asText(), claims.approvalId());
        assertEquals(expected.path("planFingerprint").asText(), claims.planFingerprint());
        assertEquals(expected.path("approvedBy").asText(), claims.approvedBy());
        assertEquals(expected.path("approvedAtUnixMilliseconds").asLong(), claims.approvedAtUnixMilliseconds());
        assertEquals(expected.path("requestJson").asText(), claims.requestJson());
    }

    @Test void javaSignerProducesByteForByteSharedGoldenToken() throws Exception {
        JsonNode vector = new ObjectMapper().readTree(Files.readString(
                Path.of("../../conformance/mcp-approval-token-golden.json")));
        String pem = vector.path("privateKeyPem_TEST_ONLY").asText();
        String encoded = pem.replace("-----BEGIN PRIVATE KEY-----", "")
                .replace("-----END PRIVATE KEY-----", "").replaceAll("\\s", "");
        PrivateKey privateKey = KeyFactory.getInstance("RSA").generatePrivate(
                new PKCS8EncodedKeySpec(Base64.getDecoder().decode(encoded)));
        JsonNode c = vector.path("claims");
        String generated = FoundgineMcpApprovalToken.encode(c.path("approvalId").asText(),
                c.path("planFingerprint").asText(), c.path("approvedBy").asText(),
                c.path("approvedAtUnixMilliseconds").asLong(), c.path("requestJson").asText(),
                FoundgineMcpApprovalToken.rsaSha256Pkcs1Signer(c.path("keyId").asText(), privateKey));
        assertEquals(vector.path("token").asText(), generated);
    }

    @Test void rsaTokenRoundTripsAndRejectsTampering() throws Exception {
        KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA");
        generator.initialize(2048);
        KeyPair pair = generator.generateKeyPair();
        var signer = FoundgineMcpApprovalToken.rsaSha256Pkcs1Signer("interop-key-1", pair.getPrivate());
        String request = "{\"operations\":[{\"entity\":1,\"kind\":\"Create\",\"fields\":{\"2\":\"x\"}}]}";
        String token = FoundgineMcpApprovalToken.encode("approval-1", "sha256:abc", "alice", 1794592800123L, request, signer);
        var claims = FoundgineMcpApprovalToken.decodeAndVerify(token, id -> id.equals("interop-key-1") ? pair.getPublic() : null);
        assertEquals("approval-1", claims.approvalId());
        assertEquals("sha256:abc", claims.planFingerprint());
        assertEquals("alice", claims.approvedBy());
        assertEquals(1794592800123L, claims.approvedAtUnixMilliseconds());
        assertEquals(request, claims.requestJson());
        String[] parts = token.split("\\.");
        // Change a significant Base64URL character. Replacing the final two characters
        // with "AA" can be a no-op when the original RSA signature happens to end in 0x00.
        String alteredSignature = (parts[2].charAt(0) == 'A' ? "B" : "A") + parts[2].substring(1);
        String altered = parts[0] + "." + parts[1] + "." + alteredSignature;
        assertThrows(GeneralSecurityException.class,
                () -> FoundgineMcpApprovalToken.decodeAndVerify(altered, id -> pair.getPublic()));
    }
}
