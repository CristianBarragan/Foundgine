package com.foundgine.providers.tools.mcp;

import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.interfaces.RSAPrivateKey;
import java.security.spec.*;
import java.util.Base64;

/**
 * Cross-language approval-token codec. Wire form:
 * fgmcp1.BASE64URL(payload).BASE64URL(RSA-SHA256-PKCS1(signature)).
 * Payload is a newline-delimited UTF-8 record; text claims are individually base64url encoded.
 */
public final class FoundgineMcpApprovalToken {
    private FoundgineMcpApprovalToken() {}
    private static final String PREFIX = "fgmcp1";
    private static final Base64.Encoder ENCODER = Base64.getUrlEncoder().withoutPadding();
    private static final Base64.Decoder DECODER = Base64.getUrlDecoder();

    public interface Signer {
        byte[] sign(byte[] payload) throws GeneralSecurityException;
        String keyId();

        /**
         * Returns a signer pinned to one key for a single {@link #encode} call. {@code encode} reads {@link #keyId()} to build
         * the payload and then calls {@link #sign}; a signer whose active key can change (for example a
         * {@link FoundgineMcpApprovalKeyRing} during rotation) must return a snapshot so both steps use the same key.
         * Fixed-key signers can keep this default.
         */
        default Signer snapshot() { return this; }
    }
    @FunctionalInterface public interface KeyResolver { PublicKey resolve(String keyId); }
    public record Claims(String keyId, String approvalId, String planFingerprint, String approvedBy,
                         long approvedAtUnixMilliseconds, String requestJson) {}

    public static String encode(String approvalId, String fingerprint, String approvedBy,
                                 long approvedAtUnixMilliseconds, String requestJson, Signer signer)
            throws GeneralSecurityException {
        require(approvalId); require(fingerprint); require(approvedBy); require(requestJson);
        signer = java.util.Objects.requireNonNull(signer, "signer").snapshot();
        require(signer.keyId());
        String payloadText = String.join("\n", "1", b64(signer.keyId()), b64(approvalId),
                b64(fingerprint), b64(approvedBy), Long.toString(approvedAtUnixMilliseconds), b64(requestJson));
        byte[] payload = payloadText.getBytes(StandardCharsets.UTF_8);
        byte[] signature = signer.sign(payload);
        if (signature == null || signature.length == 0) throw new GeneralSecurityException("Approval token signer returned an empty signature.");
        return PREFIX + "." + ENCODER.encodeToString(payload) + "." + ENCODER.encodeToString(signature);
    }

    public static Signer rsaSha256Pkcs1Signer(String keyId, PrivateKey privateKey) {
        require(keyId); if (!(privateKey instanceof RSAPrivateKey)) throw new IllegalArgumentException("RSA private key required.");
        return new Signer() {
            @Override public String keyId() { return keyId; }
            @Override public byte[] sign(byte[] payload) throws GeneralSecurityException {
                Signature s = Signature.getInstance("SHA256withRSA"); s.initSign(privateKey); s.update(payload); return s.sign();
            }
        };
    }

    public static Claims decodeAndVerify(String token, KeyResolver keys) throws GeneralSecurityException {
        if (token == null || keys == null) throw new GeneralSecurityException("Approval token is invalid.");
        String[] parts = token.split("\\.", -1);
        if (parts.length != 3 || !PREFIX.equals(parts[0])) throw new GeneralSecurityException("Approval token format or version is invalid.");
        final byte[] payload, signature;
        try {
            payload = DECODER.decode(parts[1]);
            signature = DECODER.decode(parts[2]);
            // Base64url decoders may ignore unused pad bits. Require the unique unpadded
            // representation so changing only those bits cannot produce a second token
            // string for the same signed bytes.
            if (!ENCODER.encodeToString(payload).equals(parts[1])
                    || !ENCODER.encodeToString(signature).equals(parts[2])) {
                throw new GeneralSecurityException("Approval token encoding is not canonical.");
            }
        } catch (IllegalArgumentException ex) {
            throw new GeneralSecurityException("Approval token encoding is invalid.", ex);
        }
        final String[] f;
        try { f = StandardCharsets.UTF_8.newDecoder().decode(java.nio.ByteBuffer.wrap(payload)).toString().split("\\n", -1); }
        catch (java.nio.charset.CharacterCodingException ex) { throw new GeneralSecurityException("Approval token payload is not valid UTF-8.", ex); }
        if (f.length != 7 || !"1".equals(f[0])) throw new GeneralSecurityException("Approval token payload is invalid.");
        try {
            // Only the key id is read before authentication; remaining claims stay untrusted.
            String keyId = unb64(f[1]);
            PublicKey key = keys.resolve(keyId);
            if (key == null) throw new GeneralSecurityException("Unknown approval-token key '" + keyId + "'.");
            Signature verifier = Signature.getInstance("SHA256withRSA"); verifier.initVerify(key); verifier.update(payload);
            if (!verifier.verify(signature)) throw new GeneralSecurityException("Approval token signature is invalid.");
            String approvalId = unb64(f[2]);
            String fingerprint = unb64(f[3]);
            String approvedBy = unb64(f[4]);
            long approvedAt = Long.parseLong(f[5]);
            String request = unb64(f[6]);
            require(keyId); require(approvalId); require(fingerprint); require(approvedBy); require(request);
            return new Claims(keyId, approvalId, fingerprint, approvedBy, approvedAt, request);
        } catch (IllegalArgumentException ex) { throw new GeneralSecurityException("Approval token claims are invalid.", ex); }
    }

    private static String b64(String value) { return ENCODER.encodeToString(value.getBytes(StandardCharsets.UTF_8)); }
    private static String unb64(String value) {
        try { return StandardCharsets.UTF_8.newDecoder().decode(java.nio.ByteBuffer.wrap(DECODER.decode(value))).toString(); }
        catch (java.nio.charset.CharacterCodingException ex) { throw new IllegalArgumentException("Approval token text claim is not valid UTF-8.", ex); }
    }
    private static void require(String value) { if (value == null || value.isBlank()) throw new IllegalArgumentException("Approval token field is required."); }
}
