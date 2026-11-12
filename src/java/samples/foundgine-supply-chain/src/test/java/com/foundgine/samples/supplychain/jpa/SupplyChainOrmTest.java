package com.foundgine.samples.supplychain.jpa;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.providers.storage.orm.InMemoryOrmQuerySource;
import com.foundgine.providers.storage.orm.OrmExecutionProvider;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.generated.GeneratedFoundgineMetadata;
import com.foundgine.samples.supplychain.generated.GeneratedJpaModels;
import com.foundgine.samples.supplychain.generated.GeneratedOrmMaps;

import java.math.BigDecimal;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Set;

import org.junit.jupiter.api.Test;

/**
 * Database-free checks for the fluent {@link FoundgineOrm}: fail-closed validation, authorization on the plan
 * path, generated-model coverage, and result parity with the baseline. Test definitions only: not executed in
 * the environment that wrote them.
 */
class SupplyChainOrmTest {
    private static Authorization.Context caller(String tenant, Authorization.Role role) {
        return new Authorization.Context(tenant, Set.of(1, 2), role, true);
    }

    private static FoundgineOrm.Builder<Authorization.Context> bare() {
        return FoundgineOrm.<Authorization.Context>configure()
                .metadata(GeneratedFoundgineMetadata.build())
                .entities(GeneratedOrmMaps.entityTypes())
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE);
    }

    @Test
    void generatedMapsAndModelsCoverEveryMappedEntity() {
        var orm = SupplyChainOrm.build();
        assertEquals(3, orm.registry().all().size());
        for (var mapping : orm.registry().all()) {
            var model = orm.model(mapping);
            assertNotNull(model, "generated model for " + mapping.name());
            assertEquals("id", model.idAttribute(), mapping.name() + " identifier attribute");
        }
    }

    @Test
    void buildFailsClosedWithoutAuthorization() {
        var ex = assertThrows(IllegalStateException.class,
                () -> bare().generatedModels(GeneratedJpaModels.create()).build());
        assertTrue(ex.getMessage().contains("authorization"));
    }

    @Test
    void entityWithoutGeneratedModelIsRefusedUnlessReflectionIsOptedIn() {
        var ex = assertThrows(IllegalStateException.class,
                () -> bare().authorization(SupplyChainOrm.AUTHORIZATION).build());
        assertTrue(ex.getMessage().contains("generated model"));
        assertNotNull(bare().authorization(SupplyChainOrm.AUTHORIZATION).allowReflection().build());
    }

    @Test
    void misnamedAttributeIsRejectedAtBuildTime() {
        var ex = assertThrows(IllegalStateException.class, () -> SupplyChainOrm.configure()
                .attribute("Supplier", "Name", "doesNotExist").authorization(SupplyChainOrm.AUTHORIZATION).build());
        assertTrue(ex.getMessage().contains("doesNotExist"));
    }

    @Test
    void unknownSemanticEntityIsRejectedAtBuildTime() {
        var ex = assertThrows(IllegalStateException.class, () -> SupplyChainOrm.configure()
                .entities(java.util.Map.of("Nope", SupplierEntity.class))
                .authorization(SupplyChainOrm.AUTHORIZATION).build());
        assertTrue(ex.getMessage().contains("Nope"));
    }

    @Test
    void planRejectsRolesThePolicyDenies() {
        var orm = SupplyChainOrm.build();
        assertThrows(SemanticAuthorizationException.class, () -> orm.plan(
                caller("tenant-a", Authorization.Role.CUSTOMER), "Supplier", q -> q.select("Name")));
    }

    @Test
    void unmappedEntitiesAreRefused() {
        var orm = SupplyChainOrm.build();
        assertThrows(IllegalArgumentException.class, () -> orm.plan(
                caller("tenant-a", Authorization.Role.ANALYST), "Customer", q -> q.select("Id")));
    }

    @Test
    void fluentPlanKeepsTenantIsolationAndFieldAuthorization() {
        var orm = SupplyChainOrm.build();
        var source = new InMemoryOrmQuerySource();
        seed(orm, source, 1, "Acme Components", "US", "tenant-a");
        seed(orm, source, 2, "Other Tenant Ltd", "US", "tenant-b");

        var analyst = caller("tenant-a", Authorization.Role.ANALYST);
        var plan = orm.plan(analyst, "Supplier", q -> q.select("Id", "Name", "RiskScore").whereEq("Country", "US"));
        List<ExecutionRow> rows = new OrmExecutionProvider("orm-jpa", source, orm.registry())
                .execute(plan, SupplyChainPolicy.executionContext(analyst), CancellationToken.NONE).rows();
        assertEquals(1, rows.size(), "only the caller's tenant is visible");
        assertEquals("Acme Components", rows.get(0).values().get("name"));

        var operator = caller("tenant-a", Authorization.Role.WAREHOUSE_OPERATOR);
        var operatorPlan = orm.plan(operator, "Supplier", q -> q.select("Id", "Name", "RiskScore"));
        var operatorRows = new OrmExecutionProvider("orm-jpa", source, orm.registry())
                .execute(operatorPlan, SupplyChainPolicy.executionContext(operator), CancellationToken.NONE).rows();
        assertFalse(operatorRows.get(0).values().containsKey("riskScore"), "unauthorized field is dropped");
    }

    @Test
    void loweredReadCarriesAStablePlanFingerprint() {
        var orm = SupplyChainOrm.build();
        var analyst = caller("tenant-a", Authorization.Role.ANALYST);
        var first = orm.lower(analyst, orm.from("Supplier").select("Id", "Name").whereEq("Country", "US").build());
        var again = orm.lower(analyst, orm.from("Supplier").select("Id", "Name").whereEq("Country", "US").build());
        var other = orm.lower(analyst, orm.from("Supplier").select("Id", "Name").whereEq("Country", "DE").build());
        assertFalse(first.fingerprint().isBlank());
        assertEquals(first.fingerprint(), again.fingerprint(), "same operation, same fingerprint");
        // A different filter value may or may not change the fingerprint (it can be shape-only); it must not throw.
        assertNotNull(other.fingerprint());
    }

    private static void seed(FoundgineOrm<Authorization.Context> orm, InMemoryOrmQuerySource source,
            int id, String name, String country, String tenant) {
        var entity = StreamSupportHolder.entity(orm, "Supplier");
        var row = new LinkedHashMap<FieldId, Object>();
        row.put(StreamSupportHolder.field(entity, "Id"), id);
        row.put(StreamSupportHolder.field(entity, "Name"), name);
        row.put(StreamSupportHolder.field(entity, "Country"), country);
        row.put(StreamSupportHolder.field(entity, "RiskScore"), new BigDecimal("10.00"));
        row.put(StreamSupportHolder.field(entity, "TenantId"), tenant);
        source.add(entity.entityId(), row);
    }

    private static final class StreamSupportHolder {
        static com.foundgine.core.semantic.metadata.EntityMetadata entity(FoundgineOrm<?> orm, String name) {
            for (var e : orm.metadata().entities()) if (e.name().equals(name)) return e;
            throw new IllegalArgumentException(name);
        }

        static FieldId field(com.foundgine.core.semantic.metadata.EntityMetadata e, String name) {
            return e.effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase(name)).findFirst().orElseThrow().id();
        }
    }
}
