package com.foundgine.samples.supplychain.advanced.application;

import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.providers.storage.orm.jpa.JpaEntities;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;

import jakarta.persistence.EntityManager;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Create, read, update and delete for {@code Warehouse}, {@code InventoryLot} and {@code Product} through the
 * generic {@link JpaEntities} gateway of the fluent {@link FoundgineOrm}.
 *
 * <p>Reads are gated by Foundgine (role rules and the warehouse tenant predicate). Writes need
 * {@link Authorization#canWrite}; the gateway's writes are mechanical, so the scope rules are enforced here:
 *
 * <ul>
 *   <li><b>Warehouse:</b> a created warehouse is stamped with the caller's tenant (a supplied {@code tenantId} is
 *       ignored) and the tenant can never be changed. Update/delete need the warehouse to be in the caller's
 *       {@code allowedWarehouses} and visible to the tenant predicate.
 *   <li><b>InventoryLot:</b> has no tenant column, so every operation is checked through its warehouse: the
 *       warehouse must be allowed and tenant-visible, and a lot cannot be moved to another warehouse. A lot is never
 *       listed without naming its warehouse (a root read of lots has no tenant predicate).
 *   <li><b>Product:</b> shared catalogue data with no tenant; any writer may change it.
 * </ul>
 * The caller owns the {@link EntityManager}. UNVERIFIED: not compiled or run where it was written.
 */
public final class AdvancedCrud {
    private final JpaEntities entities;
    private final Authorization.Context caller;

    public AdvancedCrud(FoundgineOrm<Authorization.Context> orm, EntityManager em, Authorization.Context caller) {
        this.entities = orm.entities(caller, em);
        this.caller = caller;
    }

    /** Warehouses and products; for lots use {@link #listLots}. */
    public List<Map<String, Object>> list(String entity, Map<String, Object> equals, List<String> fields) {
        if (is(entity, "InventoryLot")) throw new IllegalArgumentException("List lots with listLots(warehouseId, ...).");
        return entities.entity(entity).read(equals, fields);
    }

    /** Lots of one warehouse the caller may see; empty when the warehouse is not allowed or not visible. */
    public List<Map<String, Object>> listLots(int warehouseId, List<String> fields) {
        if (!warehouseInScope(warehouseId)) return List.of();
        return entities.entity("InventoryLot").read(Map.of("warehouseId", warehouseId), fields);
    }

    /** One row by key, or null when it does not exist or is outside the caller's scope. */
    public Map<String, Object> get(String entity, Object id) {
        var rows = entities.entity(entity).read(Map.of("id", id), null);
        if (rows.isEmpty()) return null;
        var row = rows.get(0);
        if (is(entity, "InventoryLot") && !warehouseInScope((Integer) row.get("warehouseId"))) return null;
        return row;
    }

    public Object create(String entity, Map<String, Object> values) {
        requireWrite();
        var copy = new LinkedHashMap<>(values);
        if (is(entity, "Warehouse")) copy.put("tenantId", caller.tenantId());
        if (is(entity, "InventoryLot") && !warehouseInScope(asInt(copy.get("warehouseId")))) {
            throw new SecurityException("The warehouse is not in the caller's scope.");
        }
        return entities.entity(entity).create(copy);
    }

    /** Returns rows affected: 0 when the row does not exist or is outside the caller's scope. */
    public int update(String entity, Object id, Map<String, Object> values) {
        requireWrite();
        if (!inScope(entity, id)) return 0;
        var copy = new LinkedHashMap<>(values);
        if (is(entity, "Warehouse")) copy.remove("tenantId"); // a row never moves between tenants
        if (is(entity, "InventoryLot") && copy.containsKey("warehouseId")) {
            throw new SecurityException("A lot cannot be moved to another warehouse here.");
        }
        return entities.entity(entity).update(id, copy);
    }

    public int delete(String entity, Object id) {
        requireWrite();
        if (!inScope(entity, id)) return 0;
        return entities.entity(entity).delete(id);
    }

    private boolean inScope(String entity, Object id) {
        if (is(entity, "Warehouse")) return warehouseInScope(asInt(id));
        return get(entity, id) != null;
    }

    /** Allowed for the caller AND visible through the tenant predicate of the gated Warehouse read. */
    private boolean warehouseInScope(Integer warehouseId) {
        if (warehouseId == null || !Authorization.canReadWarehouse(caller, warehouseId)) return false;
        return !entities.entity("Warehouse").read(Map.of("id", warehouseId), List.of("id")).isEmpty();
    }

    private void requireWrite() {
        if (!Authorization.canWrite(caller)) {
            throw new SecurityException("The caller's role or read-only flag does not allow writes.");
        }
    }

    private static boolean is(String entity, String name) {
        return name.equalsIgnoreCase(entity);
    }

    private static Integer asInt(Object value) {
        return value instanceof Number n ? n.intValue() : value == null ? null : Integer.valueOf(value.toString());
    }
}
