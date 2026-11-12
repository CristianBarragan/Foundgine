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
 *   <li><b>Writes and relationships:</b> denied. The starter has no mutation path and maps no relationships.
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

    public static ConfiguredSemanticAuthorizationPolicy create(
            SupplyChainSemantics semantics, Authorization.Context caller) {
        return create(
                semantics.entity("Supplier").entityId(),
                semantics.entity("Warehouse").entityId(),
                semantics.entity("Product").entityId(),
                semantics.field("Supplier", "riskScore"),
                caller);
    }

    /** Metadata-addressed form used by {@link SupplyChainOrm} (the {@code FoundgineOrm} authorization binding). */
    public static ConfiguredSemanticAuthorizationPolicy create(
            com.foundgine.core.semantic.metadata.MetadataRegistry metadata, Authorization.Context caller) {
        var supplier = find(metadata, "Supplier");
        var riskScore = supplier.effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase("riskScore"))
                .findFirst().orElseThrow(() -> new IllegalStateException("Supplier.riskScore is not in the metadata.")).id();
        return create(supplier.entityId(), find(metadata, "Warehouse").entityId(),
                find(metadata, "Product").entityId(), riskScore, caller);
    }

    private static com.foundgine.core.semantic.metadata.EntityMetadata find(
            com.foundgine.core.semantic.metadata.MetadataRegistry metadata, String name) {
        for (var e : metadata.entities()) if (e.name().equals(name)) return e;
        throw new IllegalStateException("Entity '" + name + "' is not in the metadata.");
    }

    private static ConfiguredSemanticAuthorizationPolicy create(
            com.foundgine.core.abstractions.EntityId supplier, com.foundgine.core.abstractions.EntityId warehouse,
            com.foundgine.core.abstractions.EntityId product, com.foundgine.core.abstractions.FieldId riskScore,
            Authorization.Context caller) {
        var config = new SemanticAuthorizationConfiguration()
                .addEntityRule((ctx, entity, op) -> {
                    if (op != AuthorizationOperation.READ) return false;
                    if (entity.equals(product)) return true;
                    return (entity.equals(supplier) || entity.equals(warehouse))
                            && caller.role() != Authorization.Role.CUSTOMER;
                })
                .addFieldRule((ctx, entity, field, op) -> {
                    if (op != AuthorizationOperation.READ) return false;
                    return !field.equals(riskScore) || Authorization.canReadSupplierRisk(caller);
                })
                .addRelationshipRule((ctx, entity, relationship, op) -> false)
                .addPredicateRule((ctx, entity, op) ->
                        op == AuthorizationOperation.READ && (entity.equals(supplier) || entity.equals(warehouse))
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
