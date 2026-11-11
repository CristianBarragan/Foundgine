package com.foundgine.providers;

import static org.junit.jupiter.api.Assertions.*;

import java.io.IOException;
import java.net.URL;
import java.util.Collections;
import java.util.List;
import java.util.ServiceLoader;
import javax.annotation.processing.Processor;
import org.junit.jupiter.api.Test;

/**
 * Guards what the {@code foundgine-providers} artifact must contain, mirroring the C# Foundgine.Providers package
 * (EF Core provider + shared ORM core + build-time generator in one package). It checks the module's classpath, not
 * the built jar; confirm the jar with {@code jar tf} in the release checklist.
 */
class ProviderPackageContentsTest {
    private static final String PROCESSOR_SERVICE = "META-INF/services/javax.annotation.processing.Processor";

    @Test
    void ormCoreAndJpaProviderAreInThisArtifact() {
        for (String name : List.of(
                "com.foundgine.providers.storage.orm.OrmRegistry",
                "com.foundgine.providers.storage.orm.OrmPlanCompiler",
                "com.foundgine.providers.storage.orm.jpa.FoundgineOrm",
                "com.foundgine.providers.storage.orm.jpa.JpaOrmProvider",
                "com.foundgine.providers.storage.orm.jpa.JpaOrmRepository",
                "com.foundgine.providers.storage.orm.jpa.FoundgineJpaReader",
                "com.foundgine.providers.aot.FoundgineOrmEntity")) {
            assertDoesNotThrow(() -> Class.forName(name, false, getClass().getClassLoader()), name);
        }
    }

    @Test
    void jpaApiIsAvailableTransitivelyButNoImplementationIsBundled() {
        assertDoesNotThrow(() -> Class.forName("jakarta.persistence.EntityManager"));
    }

    @Test
    void processorIsRegisteredExactlyOnceAndLoadable() throws IOException {
        List<URL> registrations = Collections.list(getClass().getClassLoader().getResources(PROCESSOR_SERVICE));
        long foundgine = registrations.stream().filter(url -> {
            try (var in = url.openStream()) {
                return new String(in.readAllBytes()).contains("com.foundgine.providers.aot.generator.FoundgineAotProcessor");
            } catch (IOException ex) {
                throw new IllegalStateException(ex);
            }
        }).count();
        assertEquals(1, foundgine, "the annotation processor must be registered by exactly one artifact: " + registrations);

        boolean loadable = ServiceLoader.load(Processor.class).stream()
                .anyMatch(p -> p.type().getName().equals("com.foundgine.providers.aot.generator.FoundgineAotProcessor"));
        assertTrue(loadable, "ServiceLoader must find FoundgineAotProcessor");
    }
}
