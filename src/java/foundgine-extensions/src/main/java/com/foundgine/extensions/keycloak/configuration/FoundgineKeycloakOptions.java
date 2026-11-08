package com.foundgine.extensions.keycloak.configuration;

import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import java.net.URI;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.BiFunction;

/** Keycloak identity integration settings. Port of {@code FoundgineKeycloakOptions}. */
public final class FoundgineKeycloakOptions {
    // ---- Token validation (resource-server side) ----

    /** Realm issuer URL, for example {@code https://id.example.com/realms/foundgine}. */
    public String authority = "";
    /**
     * Optional OpenID discovery document URL (C# {@code MetadataAddress}). Use it when the realm is reachable on an
     * internal address that differs from the public issuer. Default: {@code {authority}/.well-known/openid-configuration}.
     */
    public String metadataAddress;
    /** The {@code aud} a token must carry (add an audience mapper to the Keycloak client). */
    public String audience = "foundgine-api";
    public boolean requireHttpsMetadata = true;
    public Duration clockSkew = Duration.ofSeconds(30);
    /** Claim holding the tenant (add a user-attribute mapper in Keycloak). Tokens without it get no Foundgine context. */
    public String tenantClaim = "tenant_id";
    /** User/group attribute that stores the tenant (read into {@link #tenantClaim} by the Keycloak mapper). */
    public String tenantAttribute = "tenant_id";
    public boolean includeRealmRoles = true;
    /** Also read {@code resource_access[audience].roles}. */
    public boolean includeClientRoles = true;
    /** How long a downloaded JWKS is trusted before it is refreshed. */
    public Duration jwksCacheDuration = Duration.ofMinutes(10);

    // ---- Warrant issuance (Keycloak identity to Foundgine authority) ----

    public String warrantIssuer = "foundgine-keycloak-bridge";
    /** Audience of issued warrants; must match what the Foundgine runtime verifies. */
    public String warrantAudience = "foundgine";
    public String warrantKeyId = "foundgine-warrant-1";
    public Duration warrantLifetime = Duration.ofMinutes(5);
    /** PEM file holding the RSA private key (2048+ bits). Prefer a mounted secret. */
    public String warrantPrivateKeyPemPath;
    public String warrantPrivateKeyPem;

    /** Keycloak role to Foundgine grants. A role not listed grants nothing; no grants means no context. */
    public final Map<String, List<FoundgineKeycloakGrant>> roleGrants = new LinkedHashMap<>();

    /** Optional tighter constraints per tenant/roles. Default: allowed tenants = [tenant], nothing else set. */
    public BiFunction<String, Collection<String>, SecurityWarrantConstraints> constraintsFactory;

    /** Optional resource scope for the context. Default: the tenant id. */
    public BiFunction<String, Collection<String>, String> resourceScopeResolver;

    /**
     * Public keys the Foundgine runtime trusts to verify warrants, keyed by warrant key id (PEM text or a path to a PEM
     * file). The host own signing key is always trusted under {@link #warrantKeyId}.
     */
    public final Map<String, String> trustedWarrantPublicKeys = new LinkedHashMap<>();

    // ---- Workers / service accounts ----

    /** Roles whose grants a worker acts with. Keep least-privilege. */
    public final List<String> workerRoles = new ArrayList<>();
    public Duration workerWarrantLifetime = Duration.ofMinutes(10);
    public String serviceClientId;
    public String serviceClientSecret;

    // ---- Keycloak Admin API (customer onboarding operations) ----

    /** Admin API base, for example {@code https://id.example.com/admin/realms/foundgine}. Default: derived from the authority. */
    public String adminBaseUrl;
    /** Realm roles the admin operations may assign. Anything else in a payload is rejected. */
    public final List<String> adminAssignableRoles = new ArrayList<>();
    /** Required actions the admin operations may set on a new user. */
    public final List<String> adminAllowedRequiredActions =
            new ArrayList<>(List.of("UPDATE_PASSWORD", "VERIFY_EMAIL", "CONFIGURE_TOTP"));

    public void validateWarrantSettings() {
        if (blank(warrantIssuer)) throw new IllegalArgumentException("WarrantIssuer is required.");
        if (blank(warrantAudience)) throw new IllegalArgumentException("WarrantAudience is required.");
        if (blank(warrantKeyId)) throw new IllegalArgumentException("WarrantKeyId is required.");
        if (warrantLifetime.compareTo(Duration.ofSeconds(1)) < 0 || warrantLifetime.compareTo(Duration.ofHours(1)) > 0)
            throw new IllegalArgumentException("Warrants must live between 1 second and 1 hour.");
        if (workerWarrantLifetime.compareTo(Duration.ofSeconds(1)) < 0 || workerWarrantLifetime.compareTo(Duration.ofHours(1)) > 0)
            throw new IllegalArgumentException("WorkerWarrantLifetime must be between 1 second and 1 hour.");
        if (roleGrants.isEmpty())
            throw new IllegalArgumentException("RoleGrants must map at least one Keycloak role to grants.");
        for (var role : roleGrants.entrySet())
            for (var g : role.getValue()) {
                if (blank(g.capability) || blank(g.operation))
                    throw new IllegalArgumentException("Role \u0027" + role.getKey() + "\u0027 has a grant without Capability/Operation.");
                // The runtime matches resource scopes by exact string, so "*" is a literal that never matches a real
                // scope and would silently deny everything. An EMPTY list means any scope; tenant isolation still
                // comes from the warrant AllowedTenants constraint.
                if (g.resourceScopes.stream().anyMatch(r -> blank(r) || r.equals("*")))
                    throw new IllegalArgumentException("Role \u0027" + role.getKey() + "\u0027 grants " + g.capability + ":" + g.operation
                            + " with ResourceScopes containing \u0027*\u0027 or a blank entry. \u0027*\u0027 is not a wildcard; leave"
                            + " ResourceScopes empty for any scope within the tenant, or list exact scopes.");
            }
    }

    public void validateAuthenticationSettings() {
        URI uri;
        try {
            uri = URI.create(authority);
        } catch (RuntimeException ex) {
            throw new IllegalArgumentException("Authority must be an absolute URL.");
        }
        if (!uri.isAbsolute() || uri.getHost() == null) throw new IllegalArgumentException("Authority must be an absolute URL.");
        if (requireHttpsMetadata && !"https".equalsIgnoreCase(uri.getScheme()))
            throw new IllegalArgumentException("Authority must be https unless RequireHttpsMetadata is false (development only).");
        if (!blank(metadataAddress)) {
            URI metadata;
            try {
                metadata = URI.create(metadataAddress);
            } catch (RuntimeException ex) {
                throw new IllegalArgumentException("MetadataAddress must be an absolute URL.");
            }
            if (!metadata.isAbsolute() || metadata.getHost() == null)
                throw new IllegalArgumentException("MetadataAddress must be an absolute URL.");
            if (requireHttpsMetadata && !"https".equalsIgnoreCase(metadata.getScheme()))
                throw new IllegalArgumentException("MetadataAddress must be https unless RequireHttpsMetadata is false (development only).");
        }
        if (blank(audience)) throw new IllegalArgumentException("Audience is required.");
        if (blank(tenantClaim)) throw new IllegalArgumentException("TenantClaim is required.");
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
