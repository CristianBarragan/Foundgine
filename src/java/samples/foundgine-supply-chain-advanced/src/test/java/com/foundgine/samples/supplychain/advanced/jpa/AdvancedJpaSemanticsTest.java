package com.foundgine.samples.supplychain.advanced.jpa;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.providers.storage.orm.InMemoryOrmQuerySource;
import com.foundgine.providers.storage.orm.OrmExecutionProvider;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Set;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/**
 * Runs the Foundgine half of the advanced JPA read path (fluent model -> policy -> plan -> ORM plan) against the
 * in-memory ORM source, so it needs neither PostgreSQL nor Hibernate. It checks that the fluent model's
 * Warehouse -> InventoryLot relationship drives a nested read, and that tenant isolation holds on that read.
 */
class AdvancedJpaSemanticsTest {
    private static final String PROVIDER = "orm-jpa";
    private static final List<String> WAREHOUSE_FIELDS = List.of("id", "name");
    private static final List<String> LOT_FIELDS = List.of("id", "productId", "onHand");

    private AdvancedJpaSemantics semantics;
    private InMemoryOrmQuerySource source;

    @BeforeEach
    void seedRows() {
        semantics = new AdvancedJpaSemantics(PROVIDER);
        source = new InMemoryOrmQuerySource();
        warehouse(1, "Auckland DC", "tenant-a");
        warehouse(2, "Hamilton DC", "tenant-b");
        lot(10, 1, 100, "5.00");
        lot(11, 1, 101, "7.00");
        lot(12, 2, 100, "9.00");
    }

    @Test
    void nestedReadReturnsOneRowPerLotOfTheCallersWarehouse() {
        var rows = read(caller("tenant-a", Authorization.Role.ANALYST), "tenantId", "tenant-a");
        assertEquals(2, rows.size(), "warehouse 1 has two lots; tenant-b's lot is not reachable");
    }

    @Test
    void aCallerFilterOnAnotherTenantCannotWidenAccess() {
        var rows = read(caller("tenant-a", Authorization.Role.ANALYST), "tenantId", "tenant-b");
        assertTrue(rows.isEmpty(), "the tenant predicate is ANDed onto the caller's own filter");
    }

    @Test
    void customersCannotReadWarehouses() {
        assertThrows(SemanticAuthorizationException.class,
                () -> read(caller("tenant-a", Authorization.Role.CUSTOMER), "tenantId", "tenant-a"));
    }

    private List<ExecutionRow> read(Authorization.Context caller, String filterField, Object value) {
        var plan = semantics.planWarehouseInventory(caller, WAREHOUSE_FIELDS, LOT_FIELDS, filterField, value);
        return new OrmExecutionProvider(PROVIDER, source, semantics.registry())
                .execute(plan, AdvancedJpaPolicy.executionContext(caller), CancellationToken.NONE)
                .rows();
    }

    private static Authorization.Context caller(String tenant, Authorization.Role role) {
        return new Authorization.Context(tenant, Set.of(1, 2), role, true);
    }

    private void warehouse(int id, String name, String tenant) {
        var row = new LinkedHashMap<FieldId, Object>();
        row.put(semantics.field("Warehouse", "id"), id);
        row.put(semantics.field("Warehouse", "name"), name);
        row.put(semantics.field("Warehouse", "businessUnitId"), 1);
        row.put(semantics.field("Warehouse", "tenantId"), tenant);
        source.add(semantics.entity("Warehouse").entityId(), row);
    }

    private void lot(int id, int warehouseId, int productId, String onHand) {
        var row = new LinkedHashMap<FieldId, Object>();
        row.put(semantics.field("InventoryLot", "id"), id);
        row.put(semantics.field("InventoryLot", "warehouseId"), warehouseId);
        row.put(semantics.field("InventoryLot", "productId"), productId);
        row.put(semantics.field("InventoryLot", "onHand"), new BigDecimal(onHand));
        row.put(semantics.field("InventoryLot", "reserved"), BigDecimal.ZERO);
        row.put(semantics.field("InventoryLot", "quarantined"), BigDecimal.ZERO);
        row.put(semantics.field("InventoryLot", "receivedOn"), LocalDate.of(2026, 1, 1));
        source.add(semantics.entity("InventoryLot").entityId(), row);
    }
}
