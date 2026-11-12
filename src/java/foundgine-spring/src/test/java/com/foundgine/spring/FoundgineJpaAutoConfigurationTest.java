package com.foundgine.spring;

import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

/**
 * Placeholder smoke check: confirms the auto-configuration is discoverable by name. A context-runner test with
 * the supply-chain sample (ApplicationContextRunner) is still open and needs the Spring Boot test jars.
 */
class FoundgineJpaAutoConfigurationTest {
    @Test
    void importsFileNamesTheAutoConfiguration() throws Exception {
        try (var in = getClass().getResourceAsStream(
                "/META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports")) {
            assertTrue(new String(in.readAllBytes()).contains(FoundgineJpaAutoConfiguration.class.getName()));
        }
    }
}
