package com.foundgine.extensions.keycloak.authentication;

import com.foundgine.extensions.keycloak.claims.KeycloakClaims;
import com.foundgine.extensions.keycloak.claims.KeycloakPrincipal;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.providers.json.FoundgineJson;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.Signature;
import java.time.Clock;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Validates a Keycloak access token the way the C# JwtBearer setup does: RS256 signature from the realm JWKS, issuer equal
 * to the discovered issuer (or the authority when none was discovered), the configured audience, a required {@code exp} (with clock skew) and {@code nbf} when present.
 * Tokens with {@code alg} other than RS256 (including {@code none} and HS256) are rejected outright, and the validated
 * claims are the only source of subject, tenant and roles.
 */
public final class KeycloakJwtValidator {
    private final FoundgineKeycloakOptions options;
    private final IKeycloakKeySource keys;
    private final Clock clock;

    public KeycloakJwtValidator(FoundgineKeycloakOptions options, IKeycloakKeySource keys, Clock clock) {
        this.options = Objects.requireNonNull(options, "options");
        this.keys = Objects.requireNonNull(keys, "keys");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    public KeycloakJwtValidator(FoundgineKeycloakOptions options, IKeycloakKeySource keys) {
        this(options, keys, Clock.systemUTC());
    }

    @SuppressWarnings("unchecked")
    public KeycloakPrincipal validate(String token) throws KeycloakTokenException {
        if (token == null || token.isBlank()) throw new KeycloakTokenException("Token is missing.");
        var parts = token.split("\\.", -1);
        if (parts.length != 3) throw new KeycloakTokenException("Token is not a compact JWS.");
        Map<String, Object> header;
        Map<String, Object> claims;
        byte[] signature;
        try {
            var dec = Base64.getUrlDecoder();
            header = (Map<String, Object>) object(FoundgineJson.parse(new String(dec.decode(parts[0]), StandardCharsets.UTF_8)));
            claims = (Map<String, Object>) object(FoundgineJson.parse(new String(dec.decode(parts[1]), StandardCharsets.UTF_8)));
            signature = dec.decode(parts[2]);
        } catch (IllegalArgumentException ex) {
            throw new KeycloakTokenException("Token is malformed.");
        }
        if (!"RS256".equals(header.get("alg"))) throw new KeycloakTokenException("Unsupported signing algorithm.");
        var key = keys.resolve(header.get("kid") instanceof String kid ? kid : null);
        if (key == null) throw new KeycloakTokenException("Unknown signing key.");
        try {
            var verifier = Signature.getInstance("SHA256withRSA");
            verifier.initVerify(key);
            verifier.update((parts[0] + "." + parts[1]).getBytes(StandardCharsets.US_ASCII));
            if (!verifier.verify(signature)) throw new KeycloakTokenException("Invalid token signature.");
        } catch (GeneralSecurityException ex) {
            throw new KeycloakTokenException("Invalid token signature.");
        }

        var discovered = keys.issuer();
        var expected = (discovered != null && !discovered.isBlank() ? discovered : options.authority).replaceAll("/+$", "");
        if (!(claims.get("iss") instanceof String iss) || !iss.replaceAll("/+$", "").equals(expected))
            throw new KeycloakTokenException("Invalid issuer.");
        if (!hasAudience(claims.get("aud"))) throw new KeycloakTokenException("Invalid audience.");
        long now = clock.instant().getEpochSecond();
        long skew = options.clockSkew.toSeconds();
        if (!(claims.get("exp") instanceof Number exp)) throw new KeycloakTokenException("Token has no expiry.");
        if (now > exp.longValue() + skew) throw new KeycloakTokenException("Token has expired.");
        if (claims.get("nbf") instanceof Number nbf && now + skew < nbf.longValue())
            throw new KeycloakTokenException("Token is not valid yet.");

        var subject = claims.get("sub") instanceof String s && !s.isBlank() ? s : null;
        if (subject == null) throw new KeycloakTokenException("Token has no subject.");
        var tenant = claims.get(options.tenantClaim) instanceof String t && !t.isBlank() ? t : null;
        var username = claims.get("preferred_username") instanceof String u ? u : subject;
        return new KeycloakPrincipal(subject, username, tenant, KeycloakClaims.readRoles(claims, options), claims);
    }

    private boolean hasAudience(Object aud) {
        if (aud instanceof String s) return s.equals(options.audience);
        if (aud instanceof List<?> list) return list.stream().anyMatch(a -> options.audience.equals(a));
        return false;
    }

    private static Object object(Object value) {
        if (value instanceof Map<?, ?>) return value;
        throw new IllegalArgumentException("JSON object expected.");
    }
}
