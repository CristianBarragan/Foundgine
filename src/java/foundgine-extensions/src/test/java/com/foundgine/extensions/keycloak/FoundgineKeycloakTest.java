package com.foundgine.extensions.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNotSame;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantVerifier;
import com.foundgine.extensions.keycloak.authentication.KeycloakBearerAuthorizer;
import com.foundgine.extensions.keycloak.authentication.KeycloakJwtValidator;
import com.foundgine.extensions.keycloak.authentication.KeycloakTokenException;
import com.foundgine.extensions.keycloak.claims.KeycloakPrincipal;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakGrant;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.keycloak.hosting.FoundgineKeycloakProvider;
import com.foundgine.extensions.keycloak.security.KeycloakSecurityExecutionContextProvider;
import com.foundgine.extensions.keycloak.security.KeycloakTemporalSecurityContextFactory;
import com.foundgine.extensions.keycloak.warrants.FoundgineKeycloakWarrantIssuer;
import com.foundgine.extensions.keycloak.warrants.FoundgineWarrantKeyResolver;
import com.foundgine.extensions.keycloak.warrants.PemWarrantSigningKeyProvider;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.http.GatewayHttpAuthorizer;
import com.foundgine.providers.gateway.http.GatewayHttpRequest;
import java.nio.charset.StandardCharsets;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.Signature;
import java.security.interfaces.RSAPublicKey;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class FoundgineKeycloakTest {
    private static final String AUTHORITY = "https://id.example.com/realms/foundgine";
    private static final Instant NOW = Instant.parse("2026-11-09T10:00:00Z");
    private static final Clock CLOCK = Clock.fixed(NOW, ZoneOffset.UTC);

    private final KeyPair realmKey = generate();
    private final FoundgineKeycloakOptions options = options();

    private static KeyPair generate() {
        try {
            var g = KeyPairGenerator.getInstance("RSA");
            g.initialize(2048);
            return g.generateKeyPair();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private static FoundgineKeycloakOptions options() {
        var o = new FoundgineKeycloakOptions();
        o.authority = AUTHORITY;
        o.roleGrants.put("sales", List.of(new FoundgineKeycloakGrant("crm", "read")));
        o.roleGrants.put("workflow-approver", List.of(new FoundgineKeycloakGrant("workflow", "approve", "orders")));
        o.workerRoles.add("sales");
        return o;
    }

    private static String b64(String json) { return Base64.getUrlEncoder().withoutPadding().encodeToString(json.getBytes(StandardCharsets.UTF_8)); }

    private String token(String header, String payload, KeyPair signer) throws Exception {
        var signingInput = b64(header) + "." + b64(payload);
        var s = Signature.getInstance("SHA256withRSA");
        s.initSign(signer.getPrivate());
        s.update(signingInput.getBytes(StandardCharsets.US_ASCII));
        return signingInput + "." + Base64.getUrlEncoder().withoutPadding().encodeToString(s.sign());
    }

    private static String claims(String overrides) {
        long exp = NOW.getEpochSecond() + 300;
        var base = new LinkedHashMap<String, String>();
        base.put("iss", "\"" + AUTHORITY + "\"");
        base.put("aud", "\"foundgine-api\"");
        base.put("sub", "\"user-1\"");
        base.put("exp", Long.toString(exp));
        base.put("preferred_username", "\"alice\"");
        base.put("tenant_id", "\"t1\"");
        base.put("realm_access", "{\"roles\":[\"sales\",\"offline_access\"]}");
        base.put("resource_access", "{\"foundgine-api\":{\"roles\":[\"workflow-approver\"]},\"other\":{\"roles\":[\"admin\"]}}");
        if (!overrides.isEmpty())
            for (var kv : overrides.split(";;")) {
                var i = kv.indexOf('=');
                var key = kv.substring(0, i);
                var value = kv.substring(i + 1);
                if (value.equals("<remove>")) base.remove(key); else base.put(key, value);
            }
        var sb = new StringBuilder("{");
        base.forEach((k, v) -> sb.append(sb.length() > 1 ? "," : "").append('"').append(k).append("\":").append(v));
        return sb.append('}').toString();
    }

    private KeycloakJwtValidator validator() {
        return new KeycloakJwtValidator(options, kid -> "k1".equals(kid) ? (RSAPublicKey) realmKey.getPublic() : null, CLOCK);
    }

    private String good(String overrides) throws Exception {
        return token("{\"alg\":\"RS256\",\"kid\":\"k1\"}", claims(overrides), realmKey);
    }

    @Test
    void validTokenYieldsSubjectTenantAndProjectedRoles() throws Exception {
        var p = validator().validate(good(""));
        assertEquals("user-1", p.subject());
        assertEquals("alice", p.preferredUsername());
        assertEquals("t1", p.tenant());
        // realm roles + roles of the audience client only; roles of other clients are never granted
        assertEquals(java.util.Set.of("sales", "offline_access", "workflow-approver"), p.roles());
    }

    @Test
    void rejectsBadSignatureWrongIssuerWrongAudienceAndExpiry() throws Exception {
        assertThrows(KeycloakTokenException.class,
                () -> validator().validate(token("{\"alg\":\"RS256\",\"kid\":\"k1\"}", claims(""), generate())));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(good("iss=\"https://evil.example.com/realms/foundgine\"")));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(good("aud=\"someone-else\"")));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(good("exp=" + (NOW.getEpochSecond() - 120))));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(good("exp=<remove>")));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(good("sub=<remove>")));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(good("nbf=" + (NOW.getEpochSecond() + 600))));
    }

    @Test
    void clockSkewAndAudienceArraysAreHonoured() throws Exception {
        assertNotNull(validator().validate(good("exp=" + (NOW.getEpochSecond() - 10)))); // inside the 30s skew
        assertNotNull(validator().validate(good("aud=[\"x\",\"foundgine-api\"]")));
    }

    @Test
    void rejectsNoneAndSymmetricAlgorithmsAndUnknownKeys() throws Exception {
        assertThrows(KeycloakTokenException.class, () -> validator().validate(b64("{\"alg\":\"none\"}") + "." + b64(claims("")) + "."));
        assertThrows(KeycloakTokenException.class,
                () -> validator().validate(token("{\"alg\":\"HS256\",\"kid\":\"k1\"}", claims(""), realmKey)));
        assertThrows(KeycloakTokenException.class,
                () -> validator().validate(token("{\"alg\":\"RS256\",\"kid\":\"other\"}", claims(""), realmKey)));
        assertThrows(KeycloakTokenException.class, () -> validator().validate("not-a-jwt"));
        assertThrows(KeycloakTokenException.class, () -> validator().validate(""));
    }

    @Test
    void malformedRoleClaimsYieldNoRoles() throws Exception {
        var p = validator().validate(good("realm_access=\"oops\";;resource_access=[1,2]"));
        assertTrue(p.roles().isEmpty());
        options.includeRealmRoles = false;
        var q = validator().validate(good(""));
        assertEquals(java.util.Set.of("workflow-approver"), q.roles());
    }

    private FoundgineKeycloakWarrantIssuer issuer(PemWarrantSigningKeyProvider keys) { return new FoundgineKeycloakWarrantIssuer(options, keys, CLOCK); }

    @Test
    void issuedContextCarriesGrantsTenantConstraintAndVerifiesWithTheResolver() {
        var keys = PemWarrantSigningKeyProvider.createEphemeral("foundgine-warrant-1");
        var context = issuer(keys).issue("user-1", "t1", List.of("sales", "unknown-role"));
        assertNotNull(context);
        assertEquals("t1", context.tenant());
        assertEquals("t1", context.resourceScope());
        assertEquals("foundgine", context.audience());
        var warrant = context.warrant();
        assertEquals(1, warrant.grants().size());
        assertEquals("crm", warrant.grants().get(0).capability());
        assertEquals(List.of("t1"), warrant.constraints().allowedTenants());
        assertEquals(NOW.plus(Duration.ofMinutes(5)), warrant.expiresAt());

        var resolver = FoundgineWarrantKeyResolver.create(options, keys);
        assertEquals(java.util.Set.of("foundgine-warrant-1"), resolver.keyIds());
        assertNotNull(resolver.resolve("foundgine-warrant-1"));
        assertNull(resolver.resolve("nope"));
        // the runtime verifier accepts the signature through the resolver, and rejects an untrusted key id
        SecurityWarrantVerifier.verify(warrant, resolver, NOW, options.warrantIssuer, options.warrantAudience);
        var other = FoundgineWarrantKeyResolver.create(options, PemWarrantSigningKeyProvider.createEphemeral("foundgine-warrant-1"));
        assertThrows(IllegalStateException.class,
                () -> SecurityWarrantVerifier.verify(warrant, other, NOW, options.warrantIssuer, options.warrantAudience));
    }

    @Test
    void everyIssueMintsAFreshWarrantAndNoGrantsMeansNoContext() {
        var keys = PemWarrantSigningKeyProvider.createEphemeral("k");
        var issuer = issuer(keys);
        var a = issuer.issue("u", "t1", List.of("sales"));
        var b = issuer.issue("u", "t1", List.of("sales"));
        assertNotSame(a.warrant(), b.warrant());
        assertFalse(a.warrant().nonce().equals(b.warrant().nonce()));
        assertFalse(a.warrant().id().equals(b.warrant().id()));
        assertNull(issuer.issue("u", "t1", List.of("offline_access")));
        assertThrows(IllegalArgumentException.class, () -> issuer.issue("", "t1", List.of("sales")));
        assertThrows(IllegalArgumentException.class, () -> issuer.issue("u", " ", List.of("sales")));
    }

    @Test
    void duplicateGrantsFromSeveralRolesAreMergedAndCustomHooksApply() {
        options.roleGrants.put("sales-lead", List.of(new FoundgineKeycloakGrant("crm", "read")));
        options.resourceScopeResolver = (tenant, roles) -> tenant + "/emea";
        var keys = PemWarrantSigningKeyProvider.createEphemeral("k");
        var context = issuer(keys).issue("u", "t1", List.of("sales", "sales-lead"));
        assertEquals(1, context.warrant().grants().size());
        assertEquals("t1/emea", context.resourceScope());
    }

    @Test
    void optionsRejectWildcardScopesBadLifetimesAndInsecureAuthority() {
        var o = options();
        o.validateWarrantSettings();
        o.validateAuthenticationSettings();
        o.roleGrants.put("bad", List.of(new FoundgineKeycloakGrant("crm", "read", "*")));
        assertThrows(IllegalArgumentException.class, o::validateWarrantSettings);
        var p = options();
        p.warrantLifetime = Duration.ofHours(2);
        assertThrows(IllegalArgumentException.class, p::validateWarrantSettings);
        var q = options();
        q.authority = "http://id.example.com/realms/foundgine";
        assertThrows(IllegalArgumentException.class, q::validateAuthenticationSettings);
        q.requireHttpsMetadata = false;
        q.validateAuthenticationSettings();
        var r = options();
        r.roleGrants.clear();
        assertThrows(IllegalArgumentException.class, r::validateWarrantSettings);
    }

    @Test
    void keyResolverRejectsPrivateKeysAndShortKeysAndOwnKeyWins() throws Exception {
        var keys = PemWarrantSigningKeyProvider.createEphemeral("k");
        assertThrows(SecurityException.class, () -> new FoundgineWarrantKeyResolver(Map.of("x", "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----")));
        var g = KeyPairGenerator.getInstance("RSA");
        g.initialize(1024);
        var shortPem = "-----BEGIN PUBLIC KEY-----\n" + Base64.getMimeEncoder().encodeToString(g.generateKeyPair().getPublic().getEncoded())
                + "\n-----END PUBLIC KEY-----";
        assertThrows(SecurityException.class, () -> new FoundgineWarrantKeyResolver(Map.of("short", shortPem)));
        var o = options();
        o.trustedWarrantPublicKeys.put("k", keys.exportPublicKeyPem());
        // the host own signing key always wins for its key id (same as the C# indexer assignment)
        var resolver = FoundgineWarrantKeyResolver.create(o, keys);
        assertEquals(java.util.Set.of("k"), resolver.keyIds());
        var expected = Base64.getMimeDecoder().decode(keys.exportPublicKeyPem().replaceAll("-----[A-Z ]+-----", "").trim());
        assertTrue(java.util.Arrays.equals(expected, resolver.resolve("k").getEncoded()));
    }

    @Test
    void contextProviderUsesOnlyTheValidatedPrincipal() {
        var keys = PemWarrantSigningKeyProvider.createEphemeral("k");
        var issuer = issuer(keys);
        var withTenant = new KeycloakPrincipal("u", "alice", "t1", java.util.Set.of("sales"), Map.of());
        var noTenant = new KeycloakPrincipal("u", "alice", null, java.util.Set.of("sales"), Map.of());
        var noGrants = new KeycloakPrincipal("u", "alice", "t1", java.util.Set.of("offline_access"), Map.of());
        assertNotNull(new KeycloakSecurityExecutionContextProvider(() -> withTenant, issuer).getSecurityExecutionContext());
        assertNull(new KeycloakSecurityExecutionContextProvider(() -> noTenant, issuer).getSecurityExecutionContext());
        assertNull(new KeycloakSecurityExecutionContextProvider(() -> noGrants, issuer).getSecurityExecutionContext());
        assertNull(new KeycloakSecurityExecutionContextProvider(() -> null, issuer).getSecurityExecutionContext());
    }

    @Test
    void temporalFactoryActsAsInitiatorWithWorkerRolesOnly() {
        var keys = PemWarrantSigningKeyProvider.createEphemeral("k");
        var factory = new KeycloakTemporalSecurityContextFactory(issuer(keys), options);
        var context = factory.create("t9", "svc-orders", CancellationToken.NONE);
        assertEquals("svc-orders", context.subject());
        assertEquals("t9", context.tenant());
        assertEquals(NOW.plus(Duration.ofMinutes(10)), context.warrant().expiresAt());
        options.workerRoles.clear();
        assertThrows(SecurityException.class, () -> factory.create("t9", "svc-orders", CancellationToken.NONE));
    }

    @Test
    void bearerAuthorizerAuthenticatesAndEvaluatesRolePolicies() throws Exception {
        var authorizer = new KeycloakBearerAuthorizer(validator()).addRolePolicy("workflow-approver", "workflow-approver");
        var ok = GatewayHttpRequest.builder("GET", "/x").header("Authorization", "Bearer " + good("")).build();
        assertEquals(GatewayHttpAuthorizer.Decision.ALLOWED, authorizer.authorize(ok));
        assertEquals("user-1", authorizer.currentPrincipal().subject());
        assertTrue(authorizer.satisfiesPolicy(ok, "workflow-approver"));
        assertFalse(authorizer.satisfiesPolicy(ok, "undefined-policy"));
        authorizer.clear();
        assertNull(authorizer.currentPrincipal());

        var missing = GatewayHttpRequest.builder("GET", "/x").build();
        assertEquals(GatewayHttpAuthorizer.Decision.UNAUTHENTICATED, authorizer.authorize(missing));
        var forged = GatewayHttpRequest.builder("GET", "/x").header("Authorization", "Bearer " + good("aud=\"nope\"")).build();
        assertEquals(GatewayHttpAuthorizer.Decision.UNAUTHENTICATED, authorizer.authorize(forged));
        assertFalse(authorizer.satisfiesPolicy(forged, "workflow-approver"));
    }

    @Test
    void providerDeclaresIdentityKindWithTenantIsolation() {
        var p = new FoundgineKeycloakProvider();
        assertEquals(FoundgineProviderKind.IDENTITY, p.kind());
        assertEquals("foundgine.keycloak.identity", p.id());
        assertTrue((p.capabilities() & com.foundgine.providers.gateway.FoundgineProviderCapability.TENANT_ISOLATION.bit()) != 0);
    }
}
