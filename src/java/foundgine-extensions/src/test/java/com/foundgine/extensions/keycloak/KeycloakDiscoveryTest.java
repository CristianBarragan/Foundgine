package com.foundgine.extensions.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.extensions.keycloak.authentication.KeycloakJwksKeySource;
import com.foundgine.extensions.keycloak.authentication.KeycloakJwtValidator;
import com.foundgine.extensions.keycloak.authentication.KeycloakTokenException;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.sun.net.httpserver.HttpServer;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.Signature;
import java.security.interfaces.RSAPublicKey;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.Base64;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

/** Increment 9: OIDC discovery ({@code MetadataAddress}) parity with the C# JwtBearer configuration. */
class KeycloakDiscoveryTest {
    private static final Instant NOW = Instant.parse("2026-11-09T10:00:00Z");
    private HttpServer server;
    private KeyPair pair;
    private final AtomicInteger certHits = new AtomicInteger();

    @AfterEach
    void stop() {
        if (server != null) server.stop(0);
    }

    private String base() {
        return "http://127.0.0.1:" + server.getAddress().getPort();
    }

    private void start(String discoveryJson, int discoveryStatus) throws Exception {
        pair = generate();
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/realms/f/.well-known/openid-configuration", ex -> reply(ex, discoveryStatus, discoveryJson == null ? "" : discoveryJson.replace("BASE", base())));
        server.createContext("/internal/keys", ex -> { certHits.incrementAndGet(); reply(ex, 200, jwks("k1")); });
        server.createContext("/realms/f/protocol/openid-connect/certs", ex -> { certHits.incrementAndGet(); reply(ex, 200, jwks("k1")); });
        server.start();
    }

    private static void reply(com.sun.net.httpserver.HttpExchange ex, int status, String body) throws java.io.IOException {
        var bytes = body.getBytes(StandardCharsets.UTF_8);
        ex.sendResponseHeaders(status, bytes.length == 0 ? -1 : bytes.length);
        if (bytes.length > 0) ex.getResponseBody().write(bytes);
        ex.close();
    }

    private String jwks(String kid) {
        var pub = (RSAPublicKey) pair.getPublic();
        var enc = Base64.getUrlEncoder().withoutPadding();
        return "{\"keys\":[{\"kty\":\"RSA\",\"use\":\"sig\",\"kid\":\"" + kid + "\",\"n\":\""
                + enc.encodeToString(unsigned(pub.getModulus().toByteArray())) + "\",\"e\":\""
                + enc.encodeToString(unsigned(pub.getPublicExponent().toByteArray())) + "\"}]}";
    }

    private static byte[] unsigned(byte[] b) {
        if (b.length > 1 && b[0] == 0) return java.util.Arrays.copyOfRange(b, 1, b.length);
        return b;
    }

    private static KeyPair generate() throws Exception {
        var g = KeyPairGenerator.getInstance("RSA");
        g.initialize(2048);
        return g.generateKeyPair();
    }

    private FoundgineKeycloakOptions options() {
        var o = new FoundgineKeycloakOptions();
        o.authority = base() + "/realms/f";
        o.requireHttpsMetadata = false;
        return o;
    }

    private String token(String issuer) throws Exception {
        var enc = Base64.getUrlEncoder().withoutPadding();
        var header = enc.encodeToString("{\"alg\":\"RS256\",\"kid\":\"k1\"}".getBytes(StandardCharsets.UTF_8));
        var claims = enc.encodeToString(("{\"iss\":\"" + issuer + "\",\"aud\":\"foundgine-api\",\"sub\":\"u1\",\"tenant_id\":\"t1\",\"exp\":"
                + (NOW.getEpochSecond() + 300) + "}").getBytes(StandardCharsets.UTF_8));
        var sig = Signature.getInstance("SHA256withRSA");
        sig.initSign(pair.getPrivate());
        sig.update((header + "." + claims).getBytes(StandardCharsets.US_ASCII));
        return header + "." + claims + "." + enc.encodeToString(sig.sign());
    }

    @Test
    void discovery_supplies_jwks_uri_and_issuer() throws Exception {
        start("{\"issuer\":\"https://public.example.com/realms/f\",\"jwks_uri\":\"BASE/internal/keys\"}", 200);
        var source = new KeycloakJwksKeySource(options());

        assertNotNull(source.resolve("k1"));
        assertEquals("https://public.example.com/realms/f", source.issuer());
        assertEquals(1, certHits.get());
    }

    @Test
    void validator_compares_the_discovered_issuer_not_the_internal_authority() throws Exception {
        start("{\"issuer\":\"https://public.example.com/realms/f\",\"jwks_uri\":\"BASE/internal/keys\"}", 200);
        var validator = new KeycloakJwtValidator(options(), new KeycloakJwksKeySource(options()), Clock.fixed(NOW, ZoneOffset.UTC));

        assertEquals("u1", validator.validate(token("https://public.example.com/realms/f")).subject());
        // the internal authority is no longer an acceptable issuer once discovery published a different one
        assertThrows(KeycloakTokenException.class, () -> validator.validate(token(base() + "/realms/f")));
    }

    @Test
    void falls_back_to_the_keycloak_certs_endpoint_when_discovery_is_unavailable_and_not_explicit() throws Exception {
        start(null, 404);
        var o = options();
        var source = new KeycloakJwksKeySource(o);

        assertNotNull(source.resolve("k1"));
        assertNull(source.issuer());
        var validator = new KeycloakJwtValidator(o, source, Clock.fixed(NOW, ZoneOffset.UTC));
        assertEquals("u1", validator.validate(token(o.authority)).subject());
    }

    @Test
    void explicit_metadata_address_never_falls_back_to_the_certs_endpoint() throws Exception {
        start(null, 404);
        var o = options();
        o.metadataAddress = base() + "/realms/f/.well-known/openid-configuration";
        var source = new KeycloakJwksKeySource(o);

        assertNull(source.resolve("k1"));
        assertEquals(0, certHits.get());
    }

    @Test
    void explicit_metadata_address_is_used_for_discovery() throws Exception {
        start("{\"issuer\":\"https://id.example.com/realms/f\",\"jwks_uri\":\"BASE/internal/keys\"}", 200);
        var o = options();
        o.authority = "https://id.example.com/realms/f"; // public name; not reachable from here
        o.metadataAddress = base() + "/realms/f/.well-known/openid-configuration";
        var source = new KeycloakJwksKeySource(o);

        assertNotNull(source.resolve("k1"));
        assertEquals("https://id.example.com/realms/f", source.issuer());
    }

    @Test
    void insecure_jwks_uri_is_rejected_when_https_is_required() throws Exception {
        start("{\"issuer\":\"https://public.example.com/realms/f\",\"jwks_uri\":\"BASE/internal/keys\"}", 200);
        var o = options();
        o.requireHttpsMetadata = true;
        o.metadataAddress = base() + "/realms/f/.well-known/openid-configuration"; // http -> would fail validation, so bypass it
        var source = new KeycloakJwksKeySource(o);

        assertNull(source.resolve("k1"));
        assertNull(source.issuer());
        assertEquals(0, certHits.get());
    }

    @Test
    void metadata_address_is_validated_like_the_authority() {
        var o = new FoundgineKeycloakOptions();
        o.authority = "https://id.example.com/realms/f";

        o.metadataAddress = "not a url";
        assertThrows(IllegalArgumentException.class, o::validateAuthenticationSettings);

        o.metadataAddress = "http://keycloak.internal/realms/f/.well-known/openid-configuration";
        assertThrows(IllegalArgumentException.class, o::validateAuthenticationSettings);

        o.requireHttpsMetadata = false;
        o.validateAuthenticationSettings();

        o.metadataAddress = "https://keycloak.internal/realms/f/.well-known/openid-configuration";
        o.requireHttpsMetadata = true;
        o.validateAuthenticationSettings();
        assertTrue(true);
    }
}
