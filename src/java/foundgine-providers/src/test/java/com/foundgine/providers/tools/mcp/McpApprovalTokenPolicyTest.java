package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;
import java.time.Duration;
import java.util.HashSet;
import java.util.Set;
import org.junit.jupiter.api.Test;

class McpApprovalTokenPolicyTest {
    @Test void expiresFutureDatesAndConsumesExactlyOnce() {
        Set<String> consumed = new HashSet<>();
        long now = 1_000_000L;
        var policy = new FoundgineMcpApprovalTokenPolicy(Duration.ofMinutes(5), Duration.ofSeconds(30),
                () -> now, (id, expiry) -> consumed.add(id));
        var claims = new FoundgineMcpApprovalToken.Claims("k", "a1", "fp", "alice", now - 1_000, "{}");
        assertDoesNotThrow(() -> policy.validateAndConsume(claims));
        assertThrows(SecurityException.class, () -> policy.validateAndConsume(claims));
        assertThrows(SecurityException.class, () -> policy.validateAndConsume(
                new FoundgineMcpApprovalToken.Claims("k", "old", "fp", "alice", now - 300_001, "{}")));
        assertThrows(SecurityException.class, () -> policy.validateAndConsume(
                new FoundgineMcpApprovalToken.Claims("k", "future", "fp", "alice", now + 30_001, "{}")));
    }
}
