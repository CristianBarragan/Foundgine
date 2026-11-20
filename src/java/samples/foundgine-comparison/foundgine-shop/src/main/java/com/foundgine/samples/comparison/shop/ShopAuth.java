package com.foundgine.samples.comparison.shop;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler.Principal;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakGrant;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.keycloak.warrants.IFoundgineWarrantIssuer;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.Base64;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public final class ShopAuth {
    private ShopAuth() {}

    /** Role -> entity read capabilities. Roots a role may START at are checked in ShopGraphQL. */
    static void mapGrants(FoundgineKeycloakOptions o) {
        for (var role : List.of("manager", "customer")) {
            var grants = new java.util.ArrayList<FoundgineKeycloakGrant>();
            for (var entity : List.of("Customer", "Order", "OrderLine", "Product")) {
                var g = new FoundgineKeycloakGrant();
                g.capability = entity + ".read";
                g.operation = "read";
                grants.add(g);
            }
            o.roleGrants.put(role, grants);
        }
    }

    /**
     * Plain JWT bearer (HS256, issuer https://auth.local, audience shop-api, tenant_id required), checked by hand with
     * the JDK: no JWT library is on this module's classpath (see README for what that does to the line count).
     * Returns null for anything that is not a valid token. {@code alg} must be exactly HS256 (never "none").
     */
    static final class Jwt implements GraphQLHttpHandler.Authenticator {
        private static final ObjectMapper JSON = new ObjectMapper();
        private final byte[] key;

        Jwt(String signingKey) { this.key = signingKey.getBytes(StandardCharsets.UTF_8); }

        @Override
        public Principal authenticate(String token) {
            try {
                var parts = token.split("\\.", -1);
                if (parts.length != 3) return null;
                var header = JSON.readTree(Base64.getUrlDecoder().decode(parts[0]));
                if (!"HS256".equals(header.path("alg").asText())) return null;
                var mac = Mac.getInstance("HmacSHA256");
                mac.init(new SecretKeySpec(key, "HmacSHA256"));
                var expected = mac.doFinal((parts[0] + "." + parts[1]).getBytes(StandardCharsets.US_ASCII));
                if (!MessageDigest.isEqual(expected, Base64.getUrlDecoder().decode(parts[2]))) return null;

                var claims = JSON.readTree(Base64.getUrlDecoder().decode(parts[1]));
                long now = Instant.now().getEpochSecond();
                if (!claims.path("exp").canConvertToLong() || now > claims.path("exp").asLong() + 5) return null;
                if (claims.has("nbf") && now + 5 < claims.path("nbf").asLong()) return null;
                if (!"https://auth.local".equals(claims.path("iss").asText())) return null;
                if (!contains(claims.path("aud"), "shop-api")) return null;
                var sub = claims.path("sub").asText("");
                var tenant = claims.path("tenant_id").asText("");
                if (sub.isBlank() || tenant.isBlank()) return null;

                var roles = new HashSet<String>();
                var role = claims.path("role");
                if (role.isArray()) role.forEach(r -> roles.add(r.asText())); else if (role.isTextual()) roles.add(role.asText());
                var flat = new LinkedHashMap<String, String>();
                claims.fields().forEachRemaining(e -> { if (e.getValue().isValueNode()) flat.put(e.getKey(), e.getValue().asText()); });
                return new Principal(sub, roles, flat);
            } catch (GeneralSecurityException | RuntimeException | java.io.IOException invalid) {
                return null;
            }
        }

        private static boolean contains(JsonNode aud, String wanted) {
            if (aud.isTextual()) return wanted.equals(aud.asText());
            if (aud.isArray()) for (var a : aud) if (wanted.equals(a.asText())) return true;
            return false;
        }
    }

    /** Plain JWT -> Foundgine: validated claims become a short-lived signed warrant. The token is the only input. */
    static final class JwtSecurityContextProvider implements ISecurityExecutionContextProvider {
        private final IFoundgineWarrantIssuer issuer;

        JwtSecurityContextProvider(IFoundgineWarrantIssuer issuer) { this.issuer = issuer; }

        @Override
        public SecurityExecutionContext getSecurityExecutionContext() {
            var user = GraphQLHttpHandler.currentPrincipal();
            if (user == null || user.subject().isBlank()) return null;
            var tenant = user.claim("tenant_id");
            if (tenant == null || tenant.isBlank()) return null;
            // A fresh warrant per call: the runtime consumes each warrant's nonce once.
            return issuer.issue(user.subject(), tenant, user.roles());
        }
    }
}
