package com.foundgine.extensions.keycloak.warrants;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import java.time.Duration;
import java.util.Collection;

/** Turns an already-authenticated identity (subject, tenant, roles) into a signed, short-lived Foundgine context. */
public interface IFoundgineWarrantIssuer {
    /** @return null when the roles grant nothing: the caller has no Foundgine authority */
    SecurityExecutionContext issue(String subject, String tenant, Collection<String> roles, Duration lifetime);

    default SecurityExecutionContext issue(String subject, String tenant, Collection<String> roles) {
        return issue(subject, tenant, roles, null);
    }
}
