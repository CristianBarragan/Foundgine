package com.foundgine.spring;

import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;

/**
 * Contributes configuration to the {@link FoundgineOrm} builder. Typical implementation:
 * {@code b -> b.metadata(GeneratedFoundgineMetadata.build()).entities(GeneratedOrmMaps.entityTypes())
 * .generatedModels(GeneratedJpaModels.create())}. Define a {@code FoundgineOrm.Authorization} bean as well;
 * the build fails closed without one.
 */
@FunctionalInterface
public interface FoundgineOrmCustomizer<C> {
    void customize(FoundgineOrm.Builder<C> builder);
}
