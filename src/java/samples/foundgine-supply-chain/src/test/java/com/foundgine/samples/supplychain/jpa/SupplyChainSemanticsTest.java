package com.foundgine.samples.supplychain.jpa;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.providers.storage.orm.InMemoryOrmQuerySource;
import com.foundgine.providers.storage.orm.OrmExecutionProvider;
import com.foundgine.samples.supplychain.Authorization;

import java.math.BigDecimal;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/**
 * Runs the Foundgine half of the JPA read path (policy -> authorize -> plan -> ExecutionIR -> ORM plan) against
 * the in-memory ORM source, so it needs neither PostgreSQL nor Hibernate.
 */
class SupplyChainSemanticsTest {
    private static final String PROVIDER = "orm-jpa";
    private static final List<String> FIELDS = List.of("id", "name", "country", "riskScore");

    private SupplyChainSemantics semantics;
    private InMemoryOrmQuerySource source;

    @BeforeEach
    void seedRows() {
        semantics = new SupplyChainSemantics(PROVIDER);
        source = new InMemoryOrmQuerySource();
        supplier(1, "Acme Components", "US", "tenant-a");
        supplier(2, "Borealis Metals", "DE", "tenant-a");
        supplier(3, "Other Tenant Ltd", "US", "tenant-b");
    }

    @Test
    void readReturnsOnlyTheCallersTenant() {
        var rows = read(caller("tenant-a", Authorization.Role.ANALYST), "Supplier", "country", "US");
        assertEquals(List.of("Acme Components"), names(rows));
    }

    @Test
    void aCallerFilterOnAnotherTenantCannotWidenAccess() {
        var rows = read(caller("tenant-a", Authorization.Role.ANALYST), "Supplier", "tenantId", "tenant-b");
        assertTrue(rows.isEmpty(), "the tenant predicate is ANDed onto the caller's own filter");
    }

    @Test
    void riskScoreIsVisibleOnlyToRolesAllowedToReadIt() {
        var analyst = read(caller("tenant-a", Authorization.Role.ANALYST), "Supplier", "tenantId", "tenant-a");
        var operator = read(caller("tenant-a", Authorization.Role.WAREHOUSE_OPERATOR), "Supplier", "tenantId", "tenant-a");

        assertEquals(2, analyst.size());
        assertTrue(analyst.get(0).values().containsKey("riskScore"));
        assertEquals(2, operator.size());
        assertFalse(operator.get(0).values().containsKey("riskScore"), "unauthorized field is dropped");
    }

    @Test
    void customersCannotReadSuppliers() {
        assertThrows(SemanticAuthorizationException.class,
                () -> semantics.planRead(caller("tenant-a", Authorization.Role.CUSTOMER),
                        "Supplier", FIELDS, "tenantId", "tenant-a"));
    }

    @Test
    void entitiesWithoutAJpaMappingAreRefused() {
        assertThrows(IllegalArgumentException.class,
                () -> semantics.planRead(caller("tenant-a", Authorization.Role.ANALYST),
                        "Customer", List.of("id"), "id", 1));
    }

    @Test
    void aMissingTenantValueFailsClosed() {
        var analyst = caller("tenant-a", Authorization.Role.ANALYST);
        var plan = semantics.planRead(analyst, "Supplier", FIELDS, "country", "US");
        var provider = new OrmExecutionProvider(PROVIDER, source, semantics.registry());
        assertThrows(IllegalStateException.class,
                () -> provider.execute(plan, new ExecutionContext(Map.of()), CancellationToken.NONE));
    }

    private List<ExecutionRow> read(Authorization.Context caller, String entity, String filterField, Object value) {
        var plan = semantics.planRead(caller, entity, FIELDS, filterField, value);
        return new OrmExecutionProvider(PROVIDER, source, semantics.registry())
                .execute(plan, SupplyChainPolicy.executionContext(caller), CancellationToken.NONE)
                .rows();
    }

    private static List<Object> names(List<ExecutionRow> rows) {
        return rows.stream().map(r -> r.values().get("name")).toList();
    }

    private static Authorization.Context caller(String tenant, Authorization.Role role) {
        return new Authorization.Context(tenant, Set.of(1, 2), role, true);
    }

    private void supplier(int id, String name, String country, String tenant) {
        var row = new LinkedHashMap<FieldId, Object>();
        row.put(semantics.field("Supplier", "id"), id);
        row.put(semantics.field("Supplier", "name"), name);
        row.put(semantics.field("Supplier", "country"), country);
        row.put(semantics.field("Supplier", "riskScore"), new BigDecimal("10.00"));
        row.put(semantics.field("Supplier", "tenantId"), tenant);
        source.add(semantics.entity("Supplier").entityId(), row);
    }
}
