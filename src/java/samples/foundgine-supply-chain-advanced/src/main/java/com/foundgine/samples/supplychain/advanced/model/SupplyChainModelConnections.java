package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.FoundgineModelConnectionBuilder;
import com.foundgine.core.semantic.configuration.FoundgineModelConnectionConfiguration;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.domain.Domain.PurchaseOrder;
import com.foundgine.samples.supplychain.advanced.domain.Domain.Supplier;

/**
 * Model-to-entity connections: what a model may visit, how members correspond, and who may follow
 * the connection. A purchase order reaches its supplier through {@code supplierId}, and only a
 * caller from the same tenant as the supplier may follow it.
 */
final class SupplyChainModelConnections extends FoundgineModelConnectionConfiguration<PurchaseOrder> {
    @Override
    public void configure(FoundgineModelConnectionBuilder<PurchaseOrder> connections) {
        connections
                .hasConnection("supplier", Supplier.class)
                .maps(PurchaseOrder::supplierId, Supplier::id)
                .authorize(
                        Authorization.Context.class,
                        "SameTenantSupplier",
                        a -> a.context(Authorization.Context::tenantId).eq(a.resource(Supplier::tenantId)));
    }
}
