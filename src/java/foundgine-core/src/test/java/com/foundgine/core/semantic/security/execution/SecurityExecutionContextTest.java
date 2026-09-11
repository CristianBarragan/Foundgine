package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class SecurityExecutionContextTest {
    private static SecurityWarrant warrant() {
        return SecurityWarrant.ofDefaults(
                "w1", "issuer", "subject", "audience", List.<CapabilityGrant>of(),
                new SecurityWarrantConstraints(), Instant.now().minusSeconds(1),
                Instant.now().plusSeconds(60), "nonce", "key", null, new byte[0]);
    }

    @Test
    void authorityCachePartitionEscapesDelimiters() {
        var context = new SecurityExecutionContext(warrant(), "agent|a", "aud\\x", "tenant", "scope|x");
        String partition = context.authorityCachePartition();
        assertTrue(partition.contains("agent\\|a"));
        assertTrue(partition.contains("aud\\\\x"));
        assertTrue(partition.contains("scope\\|x"));
        assertTrue(partition.endsWith(warrant().digest()));
    }

    @Test
    void providerFailsClosedWhenContextMissing() {
        var provider = new DelegateSecurityExecutionContextProvider(() -> null);
        assertThrows(SecurityException.class, () ->
                SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                        provider, "MCP", "execution"));
    }

    @Test
    void providerReturnsHostContext() {
        var expected = new SecurityExecutionContext(warrant(), "subject", "audience");
        var provider = new DelegateSecurityExecutionContextProvider(() -> expected);
        assertSame(expected,
                SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(
                        provider, "MCP", "execution"));
    }
}
