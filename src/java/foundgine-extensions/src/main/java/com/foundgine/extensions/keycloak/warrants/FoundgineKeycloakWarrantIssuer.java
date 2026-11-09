package com.foundgine.extensions.keycloak.warrants;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantSigner;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Collection;
import java.util.HashSet;
import java.util.List;
import java.util.UUID;

public final class FoundgineKeycloakWarrantIssuer implements IFoundgineWarrantIssuer {
    private final FoundgineKeycloakOptions options;
    private final IFoundgineWarrantSigningKeyProvider keys;
    private final Clock clock;
    private final SecureRandom random = new SecureRandom();
    private final Object signGate = new Object(); // keep key use serialized, as in the C# issuer

    public FoundgineKeycloakWarrantIssuer(FoundgineKeycloakOptions options, IFoundgineWarrantSigningKeyProvider keys, Clock clock) {
        this.options = options;
        this.keys = keys;
        this.clock = clock == null ? Clock.systemUTC() : clock;
    }

    public FoundgineKeycloakWarrantIssuer(FoundgineKeycloakOptions options, IFoundgineWarrantSigningKeyProvider keys) {
        this(options, keys, null);
    }

    @Override
    public SecurityExecutionContext issue(String subject, String tenant, Collection<String> roles, Duration lifetime) {
        if (subject == null || subject.isBlank()) throw new IllegalArgumentException("subject is required");
        if (tenant == null || tenant.isBlank()) throw new IllegalArgumentException("tenant is required");

        var grants = new ArrayList<CapabilityGrant>();
        var seen = new HashSet<String>();
        for (var role : roles) {
            var mapped = options.roleGrants.get(role);
            if (mapped == null) continue;
            for (var g : mapped) {
                var scopes = List.copyOf(g.resourceScopes);
                var sorted = scopes.stream().sorted().toList();
                if (seen.add(g.capability + "|" + g.operation + "|" + String.join(",", sorted)))
                    grants.add(new CapabilityGrant(g.capability, g.operation, scopes));
            }
        }
        if (grants.isEmpty()) return null;

        var constraints = options.constraintsFactory != null ? options.constraintsFactory.apply(tenant, roles)
                : new SecurityWarrantConstraints(List.of(tenant), List.<String>of(), List.<String>of(), List.<String>of(), null, null);

        var now = clock.instant();
        var nonce = new byte[16];
        random.nextBytes(nonce);
        var unsigned = SecurityWarrant.ofDefaults(UUID.randomUUID().toString().replace("-", ""), options.warrantIssuer, subject,
                options.warrantAudience, grants, constraints, now, now.plus(lifetime != null ? lifetime : options.warrantLifetime),
                Base64.getEncoder().encodeToString(nonce), keys.keyId(), null, new byte[0]); // root warrant

        SecurityWarrant signed;
        synchronized (signGate) { signed = SecurityWarrantSigner.sign(unsigned, keys.privateKey()); }

        var scope = options.resourceScopeResolver != null ? options.resourceScopeResolver.apply(tenant, roles) : tenant;
        return new SecurityExecutionContext(signed, subject, options.warrantAudience, tenant, scope);
    }
}
