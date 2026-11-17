package com.foundgine.providers.security.redis;

import static org.junit.jupiter.api.Assertions.*;

import java.util.List;
import org.junit.jupiter.api.Test;

class RedisSecurityWarrantRevocationStoreTest {
    @Test void validatesSingleValueSequenceResponse() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> List.of("23"));
        assertEquals(23, store.currentSequence());
    }

    @Test void rejectsMalformedSequenceResponse() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> List.of("23", "extra"));
        assertThrows(IllegalStateException.class, store::currentSequence);
    }

    @Test void failsClosedWhenRedisIsUnavailable() {
        var store = new RedisSecurityWarrantRevocationStore((script, keys, args) -> { throw new IllegalStateException("offline"); });
        assertThrows(IllegalStateException.class, store::currentSequence);
    }
}
