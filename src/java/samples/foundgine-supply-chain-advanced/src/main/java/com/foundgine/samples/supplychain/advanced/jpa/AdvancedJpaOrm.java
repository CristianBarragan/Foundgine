package com.foundgine.samples.supplychain.advanced.jpa;

import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedJpaModels;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedOrmMaps;
import com.foundgine.samples.supplychain.advanced.model.SupplyChainFoundgineModel;

/**
 * Fluent {@link FoundgineOrm} configuration for the advanced sample. Unlike the starter (annotation metadata),
 * the semantic metadata comes from the fluent {@link SupplyChainFoundgineModel}, so the Warehouse -> InventoryLot
 * relationship is declared once in the model's connections and Foundgine, not a JPA association, joins it.
 * Entity maps and direct-access models are generated from {@code @FoundgineOrmEntity}.
 *
 * <p>UNVERIFIED: not compiled or run where it was written.
 */
public final class AdvancedJpaOrm {
    static final FoundgineOrm.Authorization<Authorization.Context> AUTHORIZATION =
            new FoundgineOrm.Authorization<>() {
                @Override
                public ISemanticAuthorizationPolicy policy(Authorization.Context caller, MetadataRegistry metadata) {
                    return AdvancedJpaPolicy.create(metadata, caller);
                }

                @Override
                public ExecutionContext executionContext(Authorization.Context caller) {
                    return AdvancedJpaPolicy.executionContext(caller);
                }
            };

    private AdvancedJpaOrm() {}

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
