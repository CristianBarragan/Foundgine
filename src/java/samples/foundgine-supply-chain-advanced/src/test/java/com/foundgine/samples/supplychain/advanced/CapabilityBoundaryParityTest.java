package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.providers.storage.inmemory.*;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

/** Content-level port of the C# Supply Chain CapabilityBoundaryTests. */
class CapabilityBoundaryParityTest {
    private static final EntityId SUPPLIER = new EntityId(9001);
    private static final FieldId SUPPLIER_ID = new FieldId(1);
    private static final FieldId SUPPLIER_NAME = new FieldId(2);
    private static final EntityId PURCHASE_ORDER = new EntityId(9002);
    private static final FieldId PO_ID = new FieldId(1);
    private static final FieldId PO_STATUS = new FieldId(2);
    private static final FieldId PO_SUPPLIER_FK = new FieldId(3);
    private static final RelationshipId SUPPLIER_PURCHASE_ORDERS = new RelationshipId(1);

    @Test
    void leafProjectionNeverLeaksBackingOnlyField() {
        var internal = new FieldId(3);
        var metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(SUPPLIER, "Supplier",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name"),
                        new ColumnMetadata(new ColumnId(3), "InternalCreditModelVersion")), null,
                List.of(new FieldMetadata(SUPPLIER_ID, "Id", Integer.class, ref(SUPPLIER, 1)),
                        new FieldMetadata(SUPPLIER_NAME, "Name", String.class, ref(SUPPLIER, 2))),
                ref(SUPPLIER, 1), null, false, null, null));
        var data = new InMemoryDataSet().add(new InMemoryRow(SUPPLIER,
                Map.of(SUPPLIER_ID, 1, SUPPLIER_NAME, "Kiwi Components", internal, 7)));
        var ir = new ExecutionIR(new ExecutionIRNode(0, ExecutionOperation.SCAN, SUPPLIER,
                List.of(SUPPLIER_NAME), null, null, List.of(), null, null, null),
                List.of(), new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
        var result = new InMemoryExecutionProvider(metadata, data)
                .executeAsync(new InMemoryCompiler(metadata, data).compile(ir), new ExecutionContext(Map.of()), CancellationToken.NONE)
                .toCompletableFuture().join();
        var row = assertSingle(result);
        assertEquals(1, row.effectiveCells().size());
        assertEquals(1, row.values().size());
        assertTrue(row.values().containsValue("Kiwi Components"));
        assertFalse(row.values().containsValue(7));
        assertFalse(row.effectiveCells().keySet().stream().anyMatch(k -> k.fieldId().equals(internal)));
    }

    @Test
    void traversalNeverLeaksRelationshipJoinKey() {
        var metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(SUPPLIER, "Supplier",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")), null,
                List.of(new FieldMetadata(SUPPLIER_ID, "Id", Integer.class, ref(SUPPLIER, 1)),
                        new FieldMetadata(SUPPLIER_NAME, "Name", String.class, ref(SUPPLIER, 2))),
                ref(SUPPLIER, 1), null, false, null, null));
        metadata.register(new EntityMetadata(PURCHASE_ORDER, "PurchaseOrder",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "SupplierId"),
                        new ColumnMetadata(new ColumnId(3), "Status")), null,
                List.of(new FieldMetadata(PO_ID, "Id", Integer.class, ref(PURCHASE_ORDER, 1)),
                        new FieldMetadata(PO_STATUS, "Status", String.class, ref(PURCHASE_ORDER, 3)),
                        new FieldMetadata(PO_SUPPLIER_FK, "SupplierId", Integer.class, ref(PURCHASE_ORDER, 2))),
                ref(PURCHASE_ORDER, 1), null, false, null, null));
        metadata.register(new RelationshipMetadata(SUPPLIER_PURCHASE_ORDERS, SUPPLIER, PURCHASE_ORDER,
                "PurchaseOrders", ref(SUPPLIER, 1), ref(PURCHASE_ORDER, 2)));
        var data = new InMemoryDataSet()
                .add(new InMemoryRow(SUPPLIER, Map.of(SUPPLIER_ID, 1, SUPPLIER_NAME, "Kiwi Components")))
                .add(new InMemoryRow(PURCHASE_ORDER, Map.of(PO_ID, 500, PO_STATUS, "Open", PO_SUPPLIER_FK, 1)));
        var child = new ExecutionIRNode(1, ExecutionOperation.TRAVERSE, PURCHASE_ORDER,
                List.of(PO_STATUS), SUPPLIER_PURCHASE_ORDERS, null, List.of(), null, null, null);
        var root = new ExecutionIRNode(0, ExecutionOperation.SCAN, SUPPLIER,
                List.of(SUPPLIER_NAME), null, null, List.of(child), null, null, null);
        var ir = new ExecutionIR(root, List.of(), new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
        var result = new InMemoryExecutionProvider(metadata, data)
                .executeAsync(new InMemoryCompiler(metadata, data).compile(ir), new ExecutionContext(Map.of()), CancellationToken.NONE)
                .toCompletableFuture().join();
        var row = assertSingle(result);
        assertEquals(2, row.effectiveCells().size());
        assertTrue(row.effectiveCells().values().contains("Kiwi Components"));
        assertTrue(row.effectiveCells().values().contains("Open"));
        assertFalse(row.effectiveCells().keySet().stream().anyMatch(k -> k.fieldId().equals(PO_SUPPLIER_FK)));
    }

    private static ColumnReference ref(EntityId entity, int column) {
        return new ColumnReference(entity, new ColumnId(column));
    }

    private static ExecutionRow assertSingle(ExecutionResult result) {
        assertEquals(1, result.rows().size());
        return result.rows().get(0);
    }
}
