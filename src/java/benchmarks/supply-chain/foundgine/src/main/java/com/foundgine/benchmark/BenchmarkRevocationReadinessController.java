package com.foundgine.benchmark;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
import com.foundgine.providers.security.redis.RedisSecurityWarrantRevocationStore;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
public class BenchmarkRevocationReadinessController {
    private final ISecurityWarrantRevocationStore revocations;
    private final RedisSecurityWarrantRevocationStore redis;
    public BenchmarkRevocationReadinessController(ISecurityWarrantRevocationStore revocations, RedisSecurityWarrantRevocationStore redis) {
        this.revocations = revocations; this.redis = redis;
    }
    @GetMapping("/health/ready") public ResponseEntity<?> ready() {
        try { redis.probe(); long sequence = revocations.currentSequence(); return ResponseEntity.ok(java.util.Map.of("status", "ready", "revocationSequence", sequence)); }
        catch (RuntimeException e) { return ResponseEntity.status(503).body(java.util.Map.of("status", "not-ready", "reason", "revocation-state-unavailable")); }
    }
}
