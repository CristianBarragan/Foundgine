package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.generated.GeneratedJpaModels;
import com.foundgine.samples.supplychain.generated.GeneratedOrmMaps;
import com.foundgine.samples.supplychain.model.SupplyChainFoundgineModel;

/**
 * The fluent {@link FoundgineOrm} configuration for the starter. Semantic metadata comes from the fluent
 * {@link SupplyChainFoundgineModel}; entity maps and direct-access models are generated from
 * {@code @FoundgineOrmEntity} (the persistence classes only).
 *
 * <p>UNVERIFIED: not compiled or run where it was written (no Foundgine/Jakarta jars available).
 */
public final class SupplyChainOrm {
    static final FoundgineOrm.Authorization<Authorization.Context> AUTHORIZATION =
            new FoundgineOrm.Authorization<>() {
                @Override
                public ISemanticAuthorizationPolicy policy(Authorization.Context caller, MetadataRegistry metadata) {
                    return SupplyChainPolicy.create(metadata, caller);
                }

                @Override
                public ExecutionContext executionContext(Authorization.Context caller) {
                    return SupplyChainPolicy.executionContext(caller);
                }
            };

    private SupplyChainOrm() {}

    /** Builder pre-loaded with the fluent metadata, generated entity map and models; callers may add to it. */
    public static FoundgineOrm.Builder<Authorization.Context> configure() {
        return FoundgineOrm.<Authorization.Context>configure()
                .metadata(new SupplyChainFoundgineModel().build())
                .entities(GeneratedOrmMaps.entityTypes())
                .generatedModels(GeneratedJpaModels.create())
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE);
    }

    public static FoundgineOrm<Authorization.Context> build() {
        return configure().authorization(AUTHORIZATION).build();
    }
}
