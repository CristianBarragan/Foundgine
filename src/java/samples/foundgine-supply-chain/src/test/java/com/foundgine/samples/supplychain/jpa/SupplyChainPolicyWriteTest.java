package com.foundgine.samples.supplychain.jpa;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.semantic.authorization.ConfiguredSemanticAuthorizationPolicy;
import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.model.SupplyChainFoundgineModel;
import java.util.Set;
import org.junit.jupiter.api.Test;

/** Regression coverage for the warehouse mutation exercised by the Java benchmark arm. */
class SupplyChainPolicyWriteTest {
    @Test
    void operatorCanWriteWarehouseNameButCannotWriteOtherEntitiesOrFields() {
        var metadata = new SupplyChainFoundgineModel().build();
        var warehouse = metadata.entities().stream().filter(e -> e.name().equals("Warehouse")).findFirst().orElseThrow();
        var supplier = metadata.entities().stream().filter(e -> e.name().equals("Supplier")).findFirst().orElseThrow();
        var name = warehouse.effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase("name")).findFirst().orElseThrow();
        var tenant = warehouse.effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase("tenantId")).findFirst().orElseThrow();
        var caller = new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.WAREHOUSE_OPERATOR, false);
        ConfiguredSemanticAuthorizationPolicy policy = SupplyChainPolicy.create(metadata, caller);

        assertTrue(policy.canWriteEntity(warehouse.entityId()));
        assertTrue(policy.canWriteField(warehouse.entityId(), name.id()));
        assertFalse(policy.canWriteField(warehouse.entityId(), tenant.id()));
        assertFalse(policy.canWriteEntity(supplier.entityId()));
        assertNotNull(policy.getPredicate(warehouse.entityId(), AuthorizationOperation.WRITE),
                "warehouse writes must retain the tenant predicate");
    }

    @Test
    void readOnlyOperatorCannotWriteWarehouse() {
        var metadata = new SupplyChainFoundgineModel().build();
        var warehouse = metadata.entities().stream().filter(e -> e.name().equals("Warehouse")).findFirst().orElseThrow();
        var caller = new Authorization.Context("tenant-a", Set.of(1), Authorization.Role.WAREHOUSE_OPERATOR, true);
        ConfiguredSemanticAuthorizationPolicy policy = SupplyChainPolicy.create(metadata, caller);
        assertFalse(policy.canWriteEntity(warehouse.entityId()));
    }
}
