package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.semantic.security.warrants.SecurityWarrant;

import java.util.Objects;

/**
 * Trusted host-owned security context carried with a semantic request.
 * The warrant is evidence of authority; the engine still verifies it and
 * checks it against the resolved capability at execution time.
 */
public record SecurityExecutionContext(
        SecurityWarrant warrant,
        String subject,
        String audience,
        String tenant,
        String resourceScope) {

    public SecurityExecutionContext {
        Objects.requireNonNull(warrant, "warrant");
        Objects.requireNonNull(subject, "subject");
        Objects.requireNonNull(audience, "audience");
    }

    public SecurityExecutionContext(SecurityWarrant warrant, String subject, String audience) {
        this(warrant, subject, audience, null, null);
    }

    /**
     * Stable authority partition used to prevent cross-warrant provider-plan
     * cache reuse. The full warrant digest is intentional and conservative.
     */
    public String authorityCachePartition() {
        return String.join("|",
                escape(subject),
                escape(audience),
                escape(tenant == null ? "-" : tenant),
                escape(resourceScope == null ? "-" : resourceScope),
                escape(warrant.digest()));
    }

    private static String escape(String value) {
        return value.replace("\\", "\\\\").replace("|", "\\|");
    }
}
