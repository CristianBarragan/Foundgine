package com.foundgine.samples.supplychain.advanced.jpa;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.authorization.ConfiguredSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationConfiguration;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationContext;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;

import java.util.Map;

/**
 * Read policy for the advanced JPA path (Warehouse and InventoryLot).
 *
 * <ul>
 *   <li>A {@code CUSTOMER} may read neither entity; every other role may read both.
 *   <li>{@code Warehouse} rows carry the predicate {@code resource.tenantId == context.tenantId}, ANDed onto the
 *       query by the ORM provider. {@code InventoryLot} has no tenant column, so lots are reachable only through
 *       their (tenant-filtered) warehouse and are never read as a root.
 *   <li>The Warehouse -> InventoryLot relationship is readable; writes are denied (this path is read-only).
 * </ul>
 */
public final class AdvancedJpaPolicy {
    static final String TENANT_CONTEXT_KEY = "context.tenantId";

    private AdvancedJpaPolicy() {}

    public static ConfiguredSemanticAuthorizationPolicy create(AdvancedJpaSemantics semantics, Authorization.Context caller) {
        var warehouse = semantics.entity("Warehouse").entityId();
        var lot = semantics.entity("InventoryLot").entityId();
        var config = new SemanticAuthorizationConfiguration()
                .addEntityRule((ctx, entity, op) -> op == AuthorizationOperation.READ
                        && (entity.equals(warehouse) || entity.equals(lot))
                        && caller.role() != Authorization.Role.CUSTOMER)
                .addFieldRule((ctx, entity, field, op) -> op == AuthorizationOperation.READ)
                .addRelationshipRule((ctx, entity, relationship, op) -> op == AuthorizationOperation.READ)
                .addPredicateRule((ctx, entity, op) ->
                        op == AuthorizationOperation.READ && entity.equals(warehouse) ? tenantPredicate() : null);
        return new ConfiguredSemanticAuthorizationPolicy(
                config, new SemanticAuthorizationContext(caller.tenantId(), caller.role().name(), Map.of()));
    }

    /** The values the tenant predicate reads at execution time; a missing value fails closed. */
    public static ExecutionContext executionContext(Authorization.Context caller) {
        return new ExecutionContext(Map.of(TENANT_CONTEXT_KEY, caller.tenantId()));
    }

    private static AuthorizationPredicate tenantPredicate() {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "tenantId"),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("context"), "tenantId"));
    }
}
