package com.foundgine.samples.supplychain.advanced.authorization;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.util.*;

/** Supply-chain-specific policy data; authorization mechanics remain in Foundgine Core. */
public final class SupplyChainAuthorization {
    public enum Role { CUSTOMER, ANALYST, WAREHOUSE_OPERATOR, SUPPLY_CHAIN_MANAGER }

    public static ConfiguredSemanticAuthorizationPolicy create(String tenantId, Role role, Map<String,String> claims) {
        var context = new SemanticAuthorizationContext(tenantId, role.name(), claims);
        var config = new SemanticAuthorizationConfiguration()
                .addEntityRule((ctx, id, op) -> op == AuthorizationOperation.READ
                        ? canReadEntity(id, role) : canWriteEntity(id, role, ctx.safeClaims()))
                .addFieldRule((ctx, entity, field, op) -> op == AuthorizationOperation.READ
                        ? canReadField(entity, field, role) : canWriteField(entity, field, role, ctx.safeClaims()))
                .addRelationshipRule((ctx, entity, relationship, op) -> op == AuthorizationOperation.READ
                        ? canReadRelationship(relationship, role)
                        : !readOnly(ctx.safeClaims()) && role == Role.SUPPLY_CHAIN_MANAGER)
                .addPredicateRule(SupplyChainAuthorization::predicate)
                .addOperationRule((ctx, entity, op, name) -> namedOperation(ctx, op, name, role));
        return new ConfiguredSemanticAuthorizationPolicy(config, context);
    }

    public static FieldId field(String entity, String field) { return SupplyChainSemanticModel.field(entity, field); }
    public static RelationshipId relationship(String entity, String relationship) { return SupplyChainSemanticModel.relationship(entity, relationship); }

    private static boolean canReadEntity(EntityId id, Role role) {
        if (id.equals(SupplyChainSemanticModel.PRODUCT)) return true;
        if (id.equals(SupplyChainSemanticModel.SUPPLIER) || id.equals(SupplyChainSemanticModel.CERTIFICATION)
                || id.equals(SupplyChainSemanticModel.WAREHOUSE) || id.equals(SupplyChainSemanticModel.INVENTORY_LOT))
            return role != Role.CUSTOMER;
        if (id.equals(SupplyChainSemanticModel.COMPLIANCE_INCIDENT))
            return role == Role.ANALYST || role == Role.SUPPLY_CHAIN_MANAGER;
        return role != Role.CUSTOMER;
    }

    private static boolean canReadField(EntityId entity, FieldId field, Role role) {
        if (entity.equals(SupplyChainSemanticModel.INVENTORY_LOT)
                && field.equals(field("InventoryLot", "Quarantined")))
            return role == Role.WAREHOUSE_OPERATOR || role == Role.SUPPLY_CHAIN_MANAGER;
        if (entity.equals(SupplyChainSemanticModel.SUPPLIER)
                && field.equals(field("Supplier", "RiskScore")))
            return role == Role.ANALYST || role == Role.SUPPLY_CHAIN_MANAGER;
        return true;
    }

    private static boolean canReadRelationship(RelationshipId relationship, Role role) {
        if (relationship.equals(relationship("Supplier", "certifications"))) return role != Role.CUSTOMER;
        if (relationship.equals(relationship("Supplier", "incidents"))) return role == Role.ANALYST || role == Role.SUPPLY_CHAIN_MANAGER;
        if (relationship.equals(relationship("Warehouse", "inventory"))) return role != Role.CUSTOMER;
        return true;
    }

    private static boolean canWriteEntity(EntityId entity, Role role, Map<String,String> claims) {
        if (readOnly(claims)) return false;
        // Command entities are semantic capabilities backed by domain services.
        // The domain service remains the final enforcement point for actor/tenant
        // ownership; the semantic boundary must therefore allow the command itself.
        if (entity.equals(EntityId.create("PlaceOrderCommand"))
                || entity.equals(EntityId.create("CancelOrderCommand")))
            return true;
        return role == Role.WAREHOUSE_OPERATOR || role == Role.SUPPLY_CHAIN_MANAGER;
    }
    private static boolean canWriteField(EntityId entity, FieldId field, Role role, Map<String,String> claims) {
        if (readOnly(claims)) return false;
        if (entity.equals(EntityId.create("PlaceOrderCommand"))
                || entity.equals(EntityId.create("CancelOrderCommand")))
            return true;
        if (entity.equals(SupplyChainSemanticModel.INVENTORY_LOT))
            return field.equals(field("InventoryLot", "OnHand")) || field.equals(field("InventoryLot", "Reserved"));
        return role == Role.SUPPLY_CHAIN_MANAGER;
    }

    private static AuthorizationPredicate predicate(SemanticAuthorizationContext context, EntityId entity, AuthorizationOperation op) {
        AuthorizationPredicate result = null;
        if (op == AuthorizationOperation.READ && (entity.equals(SupplyChainSemanticModel.SUPPLIER)
                || entity.equals(SupplyChainSemanticModel.WAREHOUSE)
                || entity.equals(SupplyChainSemanticModel.INVENTORY_LOT)))
            result = tenantPredicate("TenantId");
        String warehouse = context.safeClaims().get("warehouse");
        if (op == AuthorizationOperation.READ && warehouse != null) {
            AuthorizationPredicate scope = null;
            if (entity.equals(SupplyChainSemanticModel.WAREHOUSE)) scope = warehousePredicate("Id", warehouse);
            else if (entity.equals(SupplyChainSemanticModel.INVENTORY_LOT)) scope = warehousePredicate("WarehouseId", warehouse);
            if (scope != null) result = result == null ? scope : AuthorizationPredicate.and(result, scope);
        }
        return result;
    }

    private static AuthorizationDecision namedOperation(SemanticAuthorizationContext context, AuthorizationOperation op,
                                                        AuthorizationOperationName name, Role role) {
        if (op != AuthorizationOperation.WRITE || name == null || !"inventory.reconcile".equalsIgnoreCase(name.value())) return null;
        if (role != Role.SUPPLY_CHAIN_MANAGER) return AuthorizationDecision.DENIED;
        return context.safeClaims().containsKey("reason") && context.safeClaims().containsKey("change_ticket")
                ? AuthorizationDecision.ALLOWED : AuthorizationDecision.DENIED;
    }

    private static boolean readOnly(Map<String,String> claims) { return "read-only".equalsIgnoreCase(claims.get("scope")); }
    private static AuthorizationPredicate tenantPredicate(String property) {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), property),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("context"), "TenantId"));
    }
    private static AuthorizationPredicate warehousePredicate(String property, String warehouse) {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), property),
                AuthorizationPredicate.constant(warehouse));
    }
    private SupplyChainAuthorization() {}
}
