package com.foundgine.extensions;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class FoundgineExtensionRegistryParityTest {
    @Test
    void registry_is_deterministic_and_rejects_duplicate_ids() {
        var b = FoundgineExtensionRegistry.builder();
        b.add(extension("z"));
        b.add(extension("a"));
        var registry = b.build();

        assertEquals("a", registry.all().iterator().next().id());
        assertTrue(registry.find("z").isPresent());
        assertTrue(registry.find("missing").isEmpty());
        assertThrows(IllegalArgumentException.class, () -> b.add(extension("a")));
    }

    @Test
    void blank_lookup_is_fail_closed() {
        var registry = FoundgineExtensionRegistry.builder().add(extension("mcp")).build();
        assertTrue(registry.find(null).isEmpty());
        assertTrue(registry.find("").isEmpty());
        assertTrue(registry.find(" ").isEmpty());
    }

    private static FoundgineExtension extension(String id) {
        return new FoundgineExtension() {
            @Override public String id() { return id; }
        };
    }
}
