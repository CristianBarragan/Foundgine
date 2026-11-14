package com.foundgine.samples.supplychain.application;

import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.providers.storage.orm.jpa.JpaEntities;
import com.foundgine.samples.supplychain.Authorization;

import jakarta.persistence.EntityManager;

import java.util.List;
import java.util.Map;

/**
 * Create, read, update and delete for the starter's entities (Supplier, Warehouse, Product) through the
 * generic {@link JpaEntities} gateway of the fluent {@link FoundgineOrm}.
 *
 * <ul>
 *   <li><b>Read</b> is gated by Foundgine: role rules, the tenant predicate and field rules all apply.
 *   <li><b>Write</b> needs {@link Authorization#canWrite}. The gateway's writes are mechanical, so this class adds
 *       the tenant rules: a created Supplier/Warehouse is stamped with the caller's tenant (a supplied
 *       {@code tenantId} is ignored), and update/delete only touch rows the caller can read.
 * </ul>
 * The caller owns the {@link EntityManager}. UNVERIFIED: not compiled or run where it was written.
 */
public final class SupplyChainCrud {
    private static final List<String> TENANT_OWNED = List.of("Supplier", "Warehouse");

    private final JpaEntities entities;
    private final Authorization.Context caller;

    public SupplyChainCrud(FoundgineOrm<Authorization.Context> orm, EntityManager em, Authorization.Context caller) {
        this.entities = orm.entities(caller, em);
        this.caller = caller;
    }

    public List<Map<String, Object>> list(String entity, Map<String, Object> equals, List<String> fields) {
        return entities.entity(entity).read(equals, fields);
    }

    /** One row by key, or null when it does not exist or the caller may not see it. */
    public Map<String, Object> get(String entity, Object id) {
        var rows = entities.entity(entity).read(Map.of("id", id), null);
        return rows.isEmpty() ? null : rows.get(0);
    }

    public Object create(String entity, Map<String, Object> values) {
        requireWrite();
        var copy = new java.util.LinkedHashMap<>(values);
        if (isTenantOwned(entity)) copy.put("tenantId", caller.tenantId());
        return entities.entity(entity).create(copy);
    }

    /** Returns rows affected: 0 when the row does not exist or is not visible to the caller. */
    public int update(String entity, Object id, Map<String, Object> values) {
        requireWrite();
        if (get(entity, id) == null) return 0;
        var copy = new java.util.LinkedHashMap<>(values);
        copy.remove("tenantId"); // a row never moves between tenants
        return entities.entity(entity).update(id, copy);
    }

    public int delete(String entity, Object id) {
        requireWrite();
        if (get(entity, id) == null) return 0;
        return entities.entity(entity).delete(id);
    }

    private void requireWrite() {
        if (!Authorization.canWrite(caller)) {
            throw new SecurityException("The caller's role or read-only flag does not allow writes.");
        }
    }

    private static boolean isTenantOwned(String entity) {
        return TENANT_OWNED.stream().anyMatch(e -> e.equalsIgnoreCase(entity));
    }
}
