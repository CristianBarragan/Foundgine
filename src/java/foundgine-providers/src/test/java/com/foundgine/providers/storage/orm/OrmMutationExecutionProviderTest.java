package com.foundgine.providers.storage.orm;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import com.foundgine.core.semantic.planning.mutation.MutationOperation;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;

import java.util.List;
import java.util.Map;
import java.util.Set;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class OrmMutationExecutionProviderTest {
    public static class Customer {
        private Long id;
        private String name;
        private String region;

        public Long getId() { return id; }
        public void setId(Long v) { id = v; }
        public String getName() { return name; }
        public void setName(String v) { name = v; }
        public String getRegion() { return region; }
        public void setRegion(String v) { region = v; }
    }

    public static class Order {
        private Long id;
        private Long customerId;

        public Long getId() { return id; }
        public void setId(Long v) { id = v; }
        public Long getCustomerId() { return customerId; }
        public void setCustomerId(Long v) { customerId = v; }
    }

    public static class Ledger {
        private final Long amount = 5L;
        private Long id;

        public Long getId() { return id; }
        public void setId(Long v) { id = v; }
        public Long getAmount() { return amount; }
    }

    static final EntityId CUSTOMER = new EntityId(1);
    static final EntityId ORDER = new EntityId(2);
    static final EntityId LEDGER = new EntityId(3);
    static final FieldId C_ID = new FieldId(11), C_NAME = new FieldId(12), C_REGION = new FieldId(13);
    static final FieldId O_ID = new FieldId(21), O_CUSTOMER = new FieldId(22);
    static final FieldId L_ID = new FieldId(31), L_AMOUNT = new FieldId(32);
    static final ColumnId C_ID_COL = new ColumnId(101), C_NAME_COL = new ColumnId(102), C_REGION_COL = new ColumnId(103);
    static final ColumnId O_ID_COL = new ColumnId(201), O_CUSTOMER_COL = new ColumnId(202);
    static final ColumnId L_ID_COL = new ColumnId(301), L_AMOUNT_COL = new ColumnId(302);

    OrmRegistry registry;
    InMemoryOrmMutationSource source;
    OrmMutationExecutionProvider provider;

    static ColumnReference ref(EntityId e, ColumnId c) {
        return new ColumnReference(e, c);
    }

    static EntityMetadata entity(EntityId id, String name, ColumnId pk, List<FieldMetadata> fields) {
        var cols = fields.stream().map(f -> new ColumnMetadata(f.column().columnId(), f.name())).toList();
        return new EntityMetadata(id, name, cols, null, fields, ref(id, pk), null, false, null, null);
    }

    @BeforeEach
    void setUp() {
        registry = OrmRegistry.create(
                List.of(
                        entity(CUSTOMER, "Customer", C_ID_COL, List.of(
                                new FieldMetadata(C_ID, "Id", Long.class, ref(CUSTOMER, C_ID_COL)),
                                new FieldMetadata(C_NAME, "Name", String.class, ref(CUSTOMER, C_NAME_COL)),
                                new FieldMetadata(C_REGION, "Region", String.class, ref(CUSTOMER, C_REGION_COL)))),
                        entity(ORDER, "Order", O_ID_COL, List.of(
                                new FieldMetadata(O_ID, "Id", Long.class, ref(ORDER, O_ID_COL)),
                                new FieldMetadata(O_CUSTOMER, "CustomerId", Long.class, ref(ORDER, O_CUSTOMER_COL)))),
                        entity(LEDGER, "Ledger", L_ID_COL, List.of(
                                new FieldMetadata(L_ID, "Id", Long.class, ref(LEDGER, L_ID_COL)),
                                new FieldMetadata(L_AMOUNT, "Amount", Long.class, ref(LEDGER, L_AMOUNT_COL))))),
                List.of(),
                Map.of("Customer", Customer.class, "Order", Order.class, "Ledger", Ledger.class),
                Map.of("Customer.Id", "id", "Customer.Name", "name", "Customer.Region", "region",
                        "Order.Id", "id", "Order.CustomerId", "customerId",
                        "Ledger.Id", "id", "Ledger.Amount", "amount"));
        source = new InMemoryOrmMutationSource(registry);
        provider = new OrmMutationExecutionProvider("orm-test", source, registry);
    }

    static MutationEntitySchema schema(EntityId id, String name, Map<FieldId, ColumnId> fields, ColumnId pk) {
        return new MutationEntitySchema(id, name, Set.copyOf(fields.values()), fields, pk);
    }

    static final MutationEntitySchema CUSTOMER_SCHEMA = schema(CUSTOMER, "Customer",
            Map.of(C_ID, C_ID_COL, C_NAME, C_NAME_COL, C_REGION, C_REGION_COL), C_ID_COL);
    static final MutationEntitySchema ORDER_SCHEMA = schema(ORDER, "Order",
            Map.of(O_ID, O_ID_COL, O_CUSTOMER, O_CUSTOMER_COL), O_ID_COL);
    static final MutationEntitySchema LEDGER_SCHEMA = schema(LEDGER, "Ledger",
            Map.of(L_ID, L_ID_COL, L_AMOUNT, L_AMOUNT_COL), L_ID_COL);

    static ExecutionMutationIR batch(MutationOperation... ops) {
        var tmp = new ExecutionMutationIR(List.of(ops), List.of());
        return new ExecutionMutationIR(List.of(ops), tmp.deriveDependencies());
    }

    static MutationOperation createCustomer(String name, String region) {
        return new MutationOperation(CUSTOMER_SCHEMA, MutationKind.CREATE,
                List.of(new MutationFieldValue(C_NAME_COL, name), new MutationFieldValue(C_REGION_COL, region)),
                null, null, List.of(C_ID));
    }

    static SemanticFilterExpression regionIs(String r) {
        return new SemanticFieldFilter(C_REGION, SemanticFilterOperator.EQ, r);
    }

    Customer seedCustomer(long id, String name, String region) {
        var c = new Customer();
        c.setId(id);
        c.setName(name);
        c.setRegion(region);
        source.seed(c);
        return c;
    }

    @Test
    void create_returns_generated_key_and_later_operations_can_reference_it() {
        var order = new MutationOperation(ORDER_SCHEMA, MutationKind.CREATE,
                List.of(MutationFieldValue.fromPrevious(O_CUSTOMER_COL, 0, C_ID)), null, null, List.of(O_ID));
        var result = provider.executeBatch(batch(createCustomer("Ann", "N"), order), ExecutionContext.EMPTY);

        assertEquals(2, result.totalAffectedRows());
        Long customerId = (Long) result.results().get(0).returnedValues().get(C_ID);
        assertEquals(1, source.all(Customer.class).size());
        var created = (Order) source.all(Order.class).get(0);
        assertEquals(customerId, created.getCustomerId());
    }

    @Test
    void update_and_delete_apply_only_to_filtered_rows() {
        seedCustomer(1, "Ann", "N");
        seedCustomer(2, "Bob", "S");
        seedCustomer(3, "Cy", "N");

        var update = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.UPDATE,
                List.of(new MutationFieldValue(C_NAME_COL, "Renamed")), regionIs("N"), null, null);
        assertEquals(2, provider.executeBatch(batch(update), ExecutionContext.EMPTY).totalAffectedRows());
        assertEquals(2, source.all(Customer.class).stream().filter(c -> ((Customer) c).getName().equals("Renamed")).count());

        var delete = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.DELETE, List.of(), regionIs("S"), null, List.of(C_NAME));
        var r = provider.executeBatch(batch(delete), ExecutionContext.EMPTY);
        assertEquals("Bob", r.results().get(0).returnedValues().get(C_NAME));
        assertEquals(2, source.all(Customer.class).size());
    }

    @Test
    void unfiltered_update_and_delete_are_refused_before_any_write() {
        seedCustomer(1, "Ann", "N");
        var update = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.UPDATE,
                List.of(new MutationFieldValue(C_NAME_COL, "X")), null, null, null);
        var delete = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.DELETE, List.of(), null, null, null);
        // The valid create runs first in the batch, but validation rejects the whole batch up front.
        assertThrows(IllegalStateException.class,
                () -> provider.executeBatch(batch(createCustomer("Z", "Z"), update), ExecutionContext.EMPTY));
        assertThrows(IllegalStateException.class,
                () -> provider.executeBatch(batch(delete), ExecutionContext.EMPTY));
        assertEquals(1, source.all(Customer.class).size());
        assertEquals("Ann", ((Customer) source.all(Customer.class).get(0)).getName());
    }

    @Test
    void failed_batch_rolls_back_everything() {
        var c = seedCustomer(1, "Ann", "N");
        var update = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.UPDATE,
                List.of(new MutationFieldValue(C_NAME_COL, "Changed")), regionIs("N"), null, null);
        // Operation 2 references a field operation 0 never returned -> fails at run time.
        var bad = new MutationOperation(ORDER_SCHEMA, MutationKind.CREATE,
                List.of(MutationFieldValue.fromPrevious(O_CUSTOMER_COL, 0, C_NAME)), null, null, null);
        var first = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.CREATE,
                List.of(new MutationFieldValue(C_NAME_COL, "New")), null, null, List.of(C_ID));
        assertThrows(IllegalStateException.class,
                () -> provider.executeBatch(batch(first, update, bad), ExecutionContext.EMPTY));
        assertEquals(1, source.all(Customer.class).size(), "created customer must be rolled back");
        assertEquals("Ann", c.getName(), "update must be rolled back");
        assertTrue(source.all(Order.class).isEmpty());
    }

    @Test
    void upsert_inserts_then_updates_and_detects_non_unique_keys() {
        var upsert = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.UPSERT,
                List.of(new MutationFieldValue(C_ID_COL, 7L), new MutationFieldValue(C_NAME_COL, "V1")),
                null, List.of(C_ID_COL), List.of(C_ID));
        provider.executeBatch(batch(upsert), ExecutionContext.EMPTY);
        assertEquals(1, source.all(Customer.class).size());

        var again = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.UPSERT,
                List.of(new MutationFieldValue(C_ID_COL, 7L), new MutationFieldValue(C_NAME_COL, "V2")),
                null, List.of(C_ID_COL), null);
        provider.executeBatch(batch(again), ExecutionContext.EMPTY);
        assertEquals(1, source.all(Customer.class).size());
        assertEquals("V2", ((Customer) source.all(Customer.class).get(0)).getName());

        seedCustomer(8, "Dup", "N");
        seedCustomer(9, "Dup", "N");
        var byName = new MutationOperation(CUSTOMER_SCHEMA, MutationKind.UPSERT,
                List.of(new MutationFieldValue(C_NAME_COL, "Dup"), new MutationFieldValue(C_REGION_COL, "S")),
                null, List.of(C_NAME_COL), null);
        assertThrows(IllegalStateException.class, () -> provider.executeBatch(batch(byName), ExecutionContext.EMPTY));
    }

    @Test
    void read_only_fields_are_refused_and_conformance_reports_it() {
        var write = new MutationOperation(LEDGER_SCHEMA, MutationKind.CREATE,
                List.of(new MutationFieldValue(L_AMOUNT_COL, 9L)), null, null, null);
        var ex = assertThrows(UnsupportedOperationException.class,
                () -> provider.executeBatch(batch(write), ExecutionContext.EMPTY));
        assertTrue(ex.getMessage().contains("read-only"));
        assertFalse(provider.evaluate(batch(write)).violations().isEmpty());
    }

    @Test
    void conformance_claims_only_parameterized_values_and_atomicity() {
        var ok = provider.evaluate(batch(createCustomer("A", "N")));
        assertTrue(ok.violations().isEmpty());
        assertEquals(List.of("execution.parameterized-values", "mutation.atomic"), ok.satisfied());
        assertThrows(IllegalStateException.class, () -> ok.ensureSatisfied(List.of("authorization.required")));
    }
}
