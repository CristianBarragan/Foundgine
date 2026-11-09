package com.foundgine.extensions.keycloak.security;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.keycloak.warrants.IFoundgineWarrantIssuer;
import com.foundgine.extensions.temporal.security.IFoundgineTemporalSecurityContextFactory;

/**
 * Worker authority. Activities run as the initiating subject (for audit) but with the grants of
 * {@link FoundgineKeycloakOptions#workerRoles}, bounded to the workflow tenant, and short-lived. Authority is therefore the
 * worker, not a replay of the user token (which has long expired and must not be stored in workflow history).
 */
public final class KeycloakTemporalSecurityContextFactory implements IFoundgineTemporalSecurityContextFactory {
    private final IFoundgineWarrantIssuer issuer;
    private final FoundgineKeycloakOptions options;

    public KeycloakTemporalSecurityContextFactory(IFoundgineWarrantIssuer issuer, FoundgineKeycloakOptions options) {
        this.issuer = issuer;
        this.options = options;
    }

    @Override
    public SecurityExecutionContext create(String tenantId, String initiator, CancellationToken cancellationToken) {
        var context = issuer.issue(initiator, tenantId, options.workerRoles, options.workerWarrantLifetime);
        if (context == null) throw new SecurityException("WorkerRoles grant no Foundgine capabilities for this tenant.");
        return context;
    }
}
