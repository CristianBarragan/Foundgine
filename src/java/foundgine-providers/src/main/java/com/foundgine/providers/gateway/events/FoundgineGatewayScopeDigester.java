package com.foundgine.providers.gateway.events;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.HexFormat;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * Produces scope digests. Without a key the owner digest is the legacy unkeyed SHA-256 (compatible with events
 * already stored); with a key it is HMAC-SHA-256 so persisted digests cannot be guessed from known tenant/subject
 * values. Owner and tenant digests use different hash functions/keys, so one can never equal the other.
 * Output is upper-case hex, identical to the .NET implementation.
 */
public final class FoundgineGatewayScopeDigester {
    private static final HexFormat HEX = HexFormat.of().withUpperCase();
    private final byte[] ownerKey;
    private final byte[] tenantKey;

    public FoundgineGatewayScopeDigester(FoundgineGatewayEventStreamOptions options) {
        if (options == null) throw new IllegalArgumentException("options is required");
        ownerKey = options.scopeDigestKey();
        var domain = "foundgine.tenant.v1".getBytes(StandardCharsets.UTF_8);
        tenantKey = ownerKey == null ? domain : hmac(ownerKey, domain);
    }

    public String owner(String tenant, String subject) {
        if (subject == null || subject.isBlank()) throw new IllegalArgumentException("subject is required");
        var bytes = ((tenant == null ? "" : tenant) + "\0" + subject).getBytes(StandardCharsets.UTF_8);
        return HEX.formatHex(ownerKey == null ? sha256(bytes) : hmac(ownerKey, bytes));
    }

    public String tenant(String tenant) {
        return HEX.formatHex(hmac(tenantKey, (tenant == null ? "" : tenant).getBytes(StandardCharsets.UTF_8)));
    }

    private static byte[] sha256(byte[] data) {
        try { return MessageDigest.getInstance("SHA-256").digest(data); }
        catch (GeneralSecurityException e) { throw new IllegalStateException(e); }
    }
    private static byte[] hmac(byte[] key, byte[] data) {
        try {
            var mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(key, "HmacSHA256"));
            return mac.doFinal(data);
        } catch (GeneralSecurityException e) { throw new IllegalStateException(e); }
    }
}
