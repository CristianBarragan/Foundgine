package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.semantic.authorization.ConfiguredSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationConfiguration;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationContext;
import com.foundgine.samples.supplychain.Authorization;

import java.util.Map;

/**
 * The starter's real authorization policy, built from the caller's {@link Authorization.Context}.
 *
 * <ul>
 *   <li><b>Entity access:</b> a {@code CUSTOMER} may read only {@code Product}; every other role may read the
 *       three mapped entities.
 *   <li><b>Field access:</b> {@code Supplier.riskScore} needs {@link Authorization#canReadSupplierRisk}.
 *       Unauthorized fields are dropped from the plan rather than failing the request.
 *   <li><b>Row-level tenant isolation:</b> reads of {@code Supplier} and {@code Warehouse} carry the predicate
 *       {@code resource.tenantId == context.tenantId}, which the ORM provider ANDs onto the query. A caller
 *       filter on another tenant therefore returns no rows instead of that tenant's data.
 *   <li><b>Writes:</b> warehouse-name updates are allowed only to non-read-only operators/managers and remain tenant-scoped;
 *       product and supplier writes are denied. Relationships are not mapped.
 * </ul>
 *
 * <p>The tenant value is supplied at execution time through {@link #executionContext}; a missing value fails
 * closed. Warehouse-level scoping ({@code allowedWarehouses}) is not enforced here.
 */
public final class SupplyChainPolicy {
    /** Resource member (JPA attribute) and context path used by the tenant predicate. */
    static final String TENANT_ATTRIBUTE = "tenantId";
    static final String TENANT_CONTEXT_KEY = "context.tenantId";

    private SupplyChainPolicy() {}

    /** Metadata-addressed policy used by {@link SupplyChainOrm} (the {@code FoundgineOrm} authorization binding). */
    public static ConfiguredSemanticAuthorizationPolicy create(
            com.foundgine.core.semantic.metadata.MetadataRegistry metadata, Authorization.Context caller) {
        var supplier = find(metadata, "Supplier");
        var riskScore = supplier.effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase("riskScore"))
                .findFirst().orElseThrow(() -> new IllegalStateException("Supplier.riskScore is not in the metadata.")).id();
        var warehouse = find(metadata, "Warehouse");
        var warehouseName = warehouse.effectiveFields().stream()
                .filter(f -> f.name().equalsIgnoreCase("name"))
                .findFirst().orElseThrow(() -> new IllegalStateException("Warehouse.name is not in the metadata.")).id();
        return create(supplier.entityId(), warehouse.entityId(),
                find(metadata, "Product").entityId(), riskScore, warehouseName, caller);
    }

    private static com.foundgine.core.semantic.metadata.EntityMetadata find(
            com.foundgine.core.semantic.metadata.MetadataRegistry metadata, String name) {
        for (var e : metadata.entities()) if (e.name().equals(name)) return e;
        throw new IllegalStateException("Entity '" + name + "' is not in the metadata.");
    }

    private static ConfiguredSemanticAuthorizationPolicy create(
            com.foundgine.core.abstractions.EntityId supplier, com.foundgine.core.abstractions.EntityId warehouse,
            com.foundgine.core.abstractions.EntityId product, com.foundgine.core.abstractions.FieldId riskScore,
            com.foundgine.core.abstractions.FieldId warehouseName, Authorization.Context caller) {
        var config = new SemanticAuthorizationConfiguration()
                .addEntityRule((ctx, entity, op) -> {
                    if (op == AuthorizationOperation.READ) {
                        if (entity.equals(product)) return true;
                        return (entity.equals(supplier) || entity.equals(warehouse))
                                && caller.role() != Authorization.Role.CUSTOMER;
                    }
                    // The benchmark exposes one real write: an authorized operator/manager may
                    // update a warehouse belonging to their tenant. Product and supplier remain read-only.
                    return op == AuthorizationOperation.WRITE && entity.equals(warehouse)
                            && Authorization.canWrite(caller);
                })
                .addFieldRule((ctx, entity, field, op) -> {
                    if (op == AuthorizationOperation.READ)
                        return !field.equals(riskScore) || Authorization.canReadSupplierRisk(caller);
                    // Only the warehouse name is mutable through the benchmark GraphQL operation.
                    return op == AuthorizationOperation.WRITE && entity.equals(warehouse)
                            && field.equals(warehouseName) && Authorization.canWrite(caller);
                })
                .addRelationshipRule((ctx, entity, relationship, op) -> false)
                .addPredicateRule((ctx, entity, op) ->
                        (entity.equals(supplier) && op == AuthorizationOperation.READ)
                                || (entity.equals(warehouse) && (op == AuthorizationOperation.READ
                                || op == AuthorizationOperation.WRITE))
                                ? tenantPredicate()
                                : null);
        return new ConfiguredSemanticAuthorizationPolicy(
                config, new SemanticAuthorizationContext(caller.tenantId(), caller.role().name(), Map.of()));
    }

    /** The values the tenant predicate reads at execution time. */
    public static com.foundgine.core.execution.ExecutionContext executionContext(Authorization.Context caller) {
        return new com.foundgine.core.execution.ExecutionContext(Map.of(TENANT_CONTEXT_KEY, caller.tenantId()));
    }

    private static AuthorizationPredicate tenantPredicate() {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), TENANT_ATTRIBUTE),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("context"), "tenantId"));
    }
}
