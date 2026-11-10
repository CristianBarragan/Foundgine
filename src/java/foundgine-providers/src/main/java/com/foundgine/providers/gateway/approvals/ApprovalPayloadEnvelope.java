package com.foundgine.providers.gateway.approvals;

import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.DeserializationFeature;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;
import java.util.Base64;
import java.util.HashSet;
import java.util.Set;
import java.util.regex.Pattern;
import javax.crypto.Cipher;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;

/**
 * Shared AES-256-GCM approval envelope. The JSON object is persisted as jsonb, so decoding accepts
 * PostgreSQL's property reordering/whitespace while rejecting duplicate, missing, or extra fields.
 * The key id is authenticated as AAD using the stable cross-runtime domain separator.
 */
final class ApprovalPayloadEnvelope {
    private static final Pattern KEY_ID = Pattern.compile("[A-Za-z0-9._-]{1,64}");
    private static final SecureRandom RANDOM = new SecureRandom();
    private static final ObjectMapper JSON = strictMapper();
    private static final int NONCE_BYTES = 12;
    private static final int TAG_BYTES = 16;

    private ApprovalPayloadEnvelope() { }

    static String seal(String plaintext, String keyId, byte[] key) {
        byte[] nonce = new byte[NONCE_BYTES];
        RANDOM.nextBytes(nonce);
        return seal(plaintext, keyId, key, nonce);
    }

    /** Fixed-nonce overload for cross-runtime test vectors only; never reuse a nonce with a key. */
    static String seal(String plaintext, String keyId, byte[] key, byte[] nonce) {
        if (plaintext == null) throw new IllegalArgumentException("plaintext is required");
        requireKeyId(keyId);
        requireKey(key);
        if (nonce == null || nonce.length != NONCE_BYTES)
            throw new IllegalArgumentException("nonce must be 12 bytes");
        try {
            Cipher cipher = cipher(Cipher.ENCRYPT_MODE, keyId, key, nonce);
            byte[] encrypted = cipher.doFinal(plaintext.getBytes(StandardCharsets.UTF_8));
            return "{\"v\":1,\"kid\":\"" + keyId + "\",\"n\":\""
                    + Base64.getEncoder().encodeToString(nonce) + "\",\"c\":\""
                    + Base64.getEncoder().encodeToString(encrypted) + "\"}";
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException("Approval payload encryption failed.", e);
        }
    }

    static String open(String envelope, FoundgineGatewayApprovalKeyProvider keys) {
        if (envelope == null || keys == null) throw new IllegalArgumentException("envelope and keys are required");
        try {
            JsonNode root = JSON.readTree(envelope);
            if (root == null || !root.isObject() || root.size() != 4
                    || !root.has("v") || !root.get("v").isIntegralNumber() || root.get("v").intValue() != 1
                    || !root.has("kid") || !root.get("kid").isTextual()
                    || !root.has("n") || !root.get("n").isTextual()
                    || !root.has("c") || !root.get("c").isTextual())
                throw new IllegalArgumentException("Malformed or unsupported approval envelope.");
            Set<String> fields = new HashSet<>();
            root.fieldNames().forEachRemaining(fields::add);
            if (!fields.equals(Set.of("v", "kid", "n", "c")))
                throw new IllegalArgumentException("Approval envelope has unexpected fields.");

            String keyId = root.get("kid").textValue();
            requireKeyId(keyId);
            String nonceText = root.get("n").textValue();
            String cipherText = root.get("c").textValue();
            byte[] nonce = Base64.getDecoder().decode(nonceText);
            byte[] encrypted = Base64.getDecoder().decode(cipherText);
            if (!Base64.getEncoder().encodeToString(nonce).equals(nonceText)
                    || !Base64.getEncoder().encodeToString(encrypted).equals(cipherText)
                    || nonce.length != NONCE_BYTES || encrypted.length < TAG_BYTES)
                throw new IllegalArgumentException("Malformed approval envelope.");
            byte[] key = keys.key(keyId);
            requireKey(key);
            Cipher cipher = cipher(Cipher.DECRYPT_MODE, keyId, key, nonce);
            return StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(java.nio.charset.CodingErrorAction.REPORT)
                    .onUnmappableCharacter(java.nio.charset.CodingErrorAction.REPORT)
                    .decode(java.nio.ByteBuffer.wrap(cipher.doFinal(encrypted)))
                    .toString();
        } catch (Exception e) {
            if (e instanceof IllegalStateException state) throw state;
            throw new IllegalStateException("Approval payload failed authentication or has an invalid envelope.", e);
        }
    }

    static void requireKeyId(String keyId) {
        if (keyId == null || !KEY_ID.matcher(keyId).matches())
            throw new IllegalArgumentException("Key id must match [A-Za-z0-9._-]{1,64}.");
    }

    private static void requireKey(byte[] key) {
        if (key == null || key.length != 32)
            throw new IllegalStateException("Approval payload keys must be 32 bytes (AES-256).");
    }

    private static Cipher cipher(int mode, String keyId, byte[] key, byte[] nonce)
            throws GeneralSecurityException {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(mode, new SecretKeySpec(key.clone(), "AES"), new GCMParameterSpec(128, nonce));
        cipher.updateAAD(("foundgine.approval.v1|" + keyId).getBytes(StandardCharsets.UTF_8));
        return cipher;
    }

    private static ObjectMapper strictMapper() {
        var factory = new com.fasterxml.jackson.core.JsonFactory();
        factory.enable(JsonParser.Feature.STRICT_DUPLICATE_DETECTION);
        return new ObjectMapper(factory).enable(DeserializationFeature.FAIL_ON_TRAILING_TOKENS);
    }
}
