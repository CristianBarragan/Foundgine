package com.foundgine.extensions.keycloak.authentication;

import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.providers.json.FoundgineJson;
import java.io.IOException;
import java.math.BigInteger;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.interfaces.RSAPublicKey;
import java.security.spec.RSAPublicKeySpec;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Base64;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Downloads and caches the realm JWKS. Like the C# JwtBearer setup it follows OpenID discovery
 * ({@code FoundgineKeycloakOptions.metadataAddress}, default {@code {authority}/.well-known/openid-configuration}) to find
 * {@code jwks_uri} and the {@code issuer}; when no explicit metadata address is configured and discovery is unavailable it
 * falls back to Keycloak's {@code {authority}/protocol/openid-connect/certs}. An unknown key id triggers at most one
 * refresh per {@link #MIN_REFRESH} so a flood of forged key ids cannot hammer Keycloak.
 */
public final class KeycloakJwksKeySource implements IKeycloakKeySource {
    static final Duration MIN_REFRESH = Duration.ofSeconds(30);

    private final FoundgineKeycloakOptions options;
    private final HttpClient http;
    private final Clock clock;
    private final Object gate = new Object();
    private Map<String, RSAPublicKey> keys = Map.of();
    private Instant fetchedAt = Instant.MIN;
    private String discoveredIssuer;

    public KeycloakJwksKeySource(FoundgineKeycloakOptions options, HttpClient http, Clock clock) {
        this.options = options;
        this.http = http;
        this.clock = clock;
    }

    public KeycloakJwksKeySource(FoundgineKeycloakOptions options) {
        this(options, HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build(), Clock.systemUTC());
    }

    @Override
    public RSAPublicKey resolve(String keyId) {
        if (keyId == null) return null;
        synchronized (gate) {
            var now = clock.instant();
            var key = keys.get(keyId);
            boolean stale = Duration.between(fetchedAt, now).compareTo(options.jwksCacheDuration) > 0;
            if (key != null && !stale) return key;
            boolean mayRefresh = stale || Duration.between(fetchedAt, now).compareTo(MIN_REFRESH) > 0;
            if (mayRefresh) refresh(now);
            return keys.get(keyId);
        }
    }

    @Override
    public String issuer() {
        synchronized (gate) {
            return discoveredIssuer;
        }
    }

    private void refresh(Instant now) {
        try {
            fetchedAt = now; // also on failure, so outages are not retried on every request
            var base = options.authority.replaceAll("/+$", "");
            boolean explicit = options.metadataAddress != null && !options.metadataAddress.isBlank();
            var metadataUrl = explicit ? options.metadataAddress : base + "/.well-known/openid-configuration";
            String jwksUrl = null;
            var discovery = get(metadataUrl);
            if (discovery != null && FoundgineJson.parse(discovery) instanceof Map<?, ?> doc
                    && doc.get("jwks_uri") instanceof String uri && acceptableUri(uri)) {
                jwksUrl = uri;
                if (doc.get("issuer") instanceof String iss && !iss.isBlank()) discoveredIssuer = iss;
            }
            if (jwksUrl == null) {
                if (explicit) return; // an explicit metadata address never silently falls back; keep serving previous keys
                jwksUrl = base + "/protocol/openid-connect/certs";
            }
            var body = get(jwksUrl);
            if (body != null) keys = parse(body);
        } catch (RuntimeException ex) {
            // keep serving the previous keys
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
        }
    }

    private boolean acceptableUri(String uri) {
        try {
            var parsed = URI.create(uri);
            if (!parsed.isAbsolute() || parsed.getHost() == null) return false;
            return !options.requireHttpsMetadata || "https".equalsIgnoreCase(parsed.getScheme());
        } catch (RuntimeException ex) {
            return false;
        }
    }

    private String get(String url) throws InterruptedException {
        try {
            var response = http.send(HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(10)).GET().build(),
                    HttpResponse.BodyHandlers.ofString());
            return response.statusCode() == 200 ? response.body() : null;
        } catch (IOException ex) {
            return null;
        }
    }

    static Map<String, RSAPublicKey> parse(String json) {
        var result = new HashMap<String, RSAPublicKey>();
        if (!(FoundgineJson.parse(json) instanceof Map<?, ?> root) || !(root.get("keys") instanceof List<?> list)) return result;
        for (var item : list) {
            if (!(item instanceof Map<?, ?> jwk)) continue;
            if (!"RSA".equals(jwk.get("kty")) || !(jwk.get("kid") instanceof String kid)) continue;
            if (jwk.get("use") instanceof String use && !use.equals("sig")) continue;
            if (!(jwk.get("n") instanceof String n) || !(jwk.get("e") instanceof String e)) continue;
            try {
                var dec = Base64.getUrlDecoder();
                var key = (RSAPublicKey) KeyFactory.getInstance("RSA").generatePublic(
                        new RSAPublicKeySpec(new BigInteger(1, dec.decode(n)), new BigInteger(1, dec.decode(e))));
                if (key.getModulus().bitLength() >= 2048) result.put(kid, key);
            } catch (GeneralSecurityException | IllegalArgumentException ex) {
                // skip malformed keys
            }
        }
        return result;
    }
}
