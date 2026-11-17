package com.foundgine.core.semantic.security.warrants;

import static org.junit.jupiter.api.Assertions.*;

import java.time.Instant;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.Executors;
import org.junit.jupiter.api.Test;

class SecurityWarrantDigestMemoTest {
    private static SecurityWarrant warrant(String id, String nonce) {
        Instant now = Instant.now();
        return SecurityWarrant.ofDefaults(id, "issuer", "subject", "audience",
                List.of(new CapabilityGrant("read", "query")), SecurityWarrantConstraints.UNRESTRICTED,
                now.minusSeconds(60), now.plusSeconds(3600), nonce, "key-1", null, new byte[0]);
    }

    @Test void digestMatchesAFreshCanonicalComputationOnEveryRead() {
        var w = warrant("w-1", "n1");
        String expected = SecurityWarrantCanonicalizer.digest(w);
        assertEquals(expected, w.digest());
        assertEquals(expected, w.digest());
        assertSame(w.digest(), w.digest());
    }

    @Test void aModifiedCopyGetsItsOwnDigest() {
        var original = warrant("w-1", "n1");
        String originalDigest = original.digest();
        var other = warrant("w-1", "n2");

        assertNotEquals(originalDigest, other.digest());
        assertEquals(SecurityWarrantCanonicalizer.digest(other), other.digest());
        assertEquals(originalDigest, original.digest());
    }

    @Test void signingDoesNotChangeTheDigest() {
        var unsigned = warrant("w-1", "n1");
        String before = unsigned.digest();
        var signed = unsigned.withSignature(new byte[] {1, 2, 3});
        assertEquals(before, signed.digest());
    }

    @Test void moreWarrantsThanMemoSlotsStillReturnCorrectDigests() {
        var warrants = new java.util.ArrayList<SecurityWarrant>();
        for (int i = 0; i < 12; i++) warrants.add(warrant("w-" + i, "n" + i));
        for (int round = 0; round < 3; round++)
            for (var w : warrants) assertEquals(SecurityWarrantCanonicalizer.digest(w), w.digest());
    }

    @Test void concurrentReadsOnSharedWarrantsAllSeeTheCorrectDigest() throws Exception {
        var shared = warrant("shared", "n");
        String expected = SecurityWarrantCanonicalizer.digest(shared);
        var pool = Executors.newFixedThreadPool(8);
        try {
            var tasks = new java.util.ArrayList<Callable<String>>();
            for (int i = 0; i < 64; i++) tasks.add(shared::digest);
            for (var f : pool.invokeAll(tasks)) assertEquals(expected, f.get());
        } finally { pool.shutdownNow(); }
    }
}
