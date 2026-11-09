package com.foundgine.extensions.keycloak.tokens;

import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.providers.json.FoundgineJson;
import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Map;

/** Client-credentials access token for calling other Keycloak-protected services from operations. Cached until near expiry. */
public final class KeycloakServiceTokenProvider {
    private final HttpClient http;
    private final FoundgineKeycloakOptions options;
    private final Clock clock;
    private final Object gate = new Object();
    private String token;
    private Instant expires = Instant.MIN;

    public KeycloakServiceTokenProvider(HttpClient http, FoundgineKeycloakOptions options, Clock clock) {
        this.http = http;
        this.options = options;
        this.clock = clock == null ? Clock.systemUTC() : clock;
    }

    public KeycloakServiceTokenProvider(FoundgineKeycloakOptions options) {
        this(HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build(), options, null);
    }

    public String getAccessToken() throws IOException, InterruptedException {
        if (options.serviceClientId == null || options.serviceClientId.isBlank()
                || options.serviceClientSecret == null || options.serviceClientSecret.isBlank())
            throw new IllegalStateException("ServiceClientId and ServiceClientSecret must be configured.");
        synchronized (gate) {
            if (token != null && clock.instant().isBefore(expires)) return token;
            var form = "grant_type=client_credentials&client_id=" + enc(options.serviceClientId)
                    + "&client_secret=" + enc(options.serviceClientSecret);
            var request = HttpRequest.newBuilder(URI.create(options.authority.replaceAll("/+$", "") + "/protocol/openid-connect/token"))
                    .timeout(Duration.ofSeconds(15)).header("Content-Type", "application/x-www-form-urlencoded")
                    .POST(HttpRequest.BodyPublishers.ofString(form)).build();
            var response = http.send(request, HttpResponse.BodyHandlers.ofString());
            if (response.statusCode() / 100 != 2) // the body is never logged or included: it may contain secrets
                throw new IOException("Keycloak token endpoint returned " + response.statusCode() + ".");
            if (!(FoundgineJson.parse(response.body()) instanceof Map<?, ?> body) || !(body.get("access_token") instanceof String t) || t.isEmpty())
                throw new IllegalStateException("Keycloak returned no access_token.");
            long seconds = body.get("expires_in") instanceof Number n ? n.longValue() : 60;
            token = t;
            expires = clock.instant().plusSeconds(Math.max(5, seconds - 30)); // refresh 30 seconds early
            return token;
        }
    }

    private static String enc(String s) { return URLEncoder.encode(s, StandardCharsets.UTF_8); }
}
