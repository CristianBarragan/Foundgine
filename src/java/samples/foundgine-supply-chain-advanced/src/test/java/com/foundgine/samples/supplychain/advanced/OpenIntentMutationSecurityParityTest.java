package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.semantic.mutation.SemanticMutationIntentBuilder;
import com.foundgine.core.semantic.mutation.SemanticMutationPlanner;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.time.LocalDate;

/**
 * Java parity for the C# advanced sample's {@code
 * Semantic/Tests/OpenIntentMutationSecurityTests.cs} — Supply Chain mutation cases that
 * deliberately exercise the open mutation surface beyond CRUD: branching generated-value flow,
 * target filters, upsert conflicts, relationship effects and fail-closed authoring validation.
 */
class OpenIntentMutationSecurityParityTest {

    @Test
    void purchaseOrderFanOutPreservesIdentityFlowToLineAndShipment() {
        var model = SupplyChainSemanticModel.build();
        var graph =
                new SemanticMutationIntentBuilder(model)
                        .create("PurchaseOrder", "order")
                        .set("SupplierId", 1)
                        .set("WarehouseId", 1)
                        .set("Status", "Open")
                        .returns("Id")
                        .create("PurchaseOrderLine", "line")
                        .setFrom("PurchaseOrderId", "order", "Id")
                        .set("ProductId", 1)
                        .set("Quantity", BigDecimal.valueOf(100))
                        .returns("Id", "PurchaseOrderId")
                        .create("Shipment", "shipment")
                        .setFrom("PurchaseOrderId", "order", "Id")
                        .set("ExpectedArrival", LocalDate.of(2026, 9, 5))
                        .set("Status", "Planned")
                        .set("Quantity", BigDecimal.valueOf(100))
                        .returns("Id", "PurchaseOrderId")
                        .build();

        var plan = new SemanticMutationPlanner().plan(graph);

        assertEquals(3, plan.operations().size());
        assertEquals(2, plan.dependencies().size());
        plan.dependencies()
                .forEach(
                        dependency ->
                                assertEquals(0, Integer.parseInt(dependency.fromOperationId())));
        var purchaseOrderLineId =
                model.resolveEntity("PurchaseOrderLine").fields().stream()
                        .filter(f -> f.name().equals("PurchaseOrderId"))
                        .findFirst()
                        .orElseThrow()
                        .id();
        var shipmentPurchaseOrderId =
                model.resolveEntity("Shipment").fields().stream()
                        .filter(f -> f.name().equals("PurchaseOrderId"))
                        .findFirst()
                        .orElseThrow()
                        .id();
        plan.dependencies()
                .forEach(
                        dependency ->
                                assertTrue(
                                        dependency.targetField().equals(purchaseOrderLineId)
                                                || dependency
                                                        .targetField()
                                                        .equals(shipmentPurchaseOrderId)));
    }

    @Test
    void updateAndDeleteRequireTargetFilters() {
        var model = SupplyChainSemanticModel.build();

        var update =
                new SemanticMutationIntentBuilder(model)
                        .update("PurchaseOrder")
                        .set("Status", "Closed");
        var delete = new SemanticMutationIntentBuilder(model).delete("PurchaseOrder");

        assertThrows(IllegalStateException.class, update::build);
        assertThrows(IllegalStateException.class, delete::build);
    }

    @Test
    void upsertRequiresExplicitConflictSemantics() {
        var model = SupplyChainSemanticModel.build();
        var withoutConflict =
                new SemanticMutationIntentBuilder(model)
                        .upsert("PurchaseOrder")
                        .set("SupplierId", 1);

        assertThrows(IllegalStateException.class, withoutConflict::build);

        var valid =
                new SemanticMutationIntentBuilder(model)
                        .upsert("PurchaseOrder")
                        .set("SupplierId", 1)
                        .set("WarehouseId", 1)
                        .set("Status", "Open")
                        .conflict("Id")
                        .returns("Id")
                        .build();

        assertEquals(1, valid.operations().size());
        assertEquals(1, valid.operations().get(0).conflictFields().size());
    }

    @Test
    void mutationFieldAndEntityNamesAreResolvedBeforePlanning() {
        var model = SupplyChainSemanticModel.build();

        assertThrows(
                IllegalStateException.class,
                () ->
                        new SemanticMutationIntentBuilder(model)
                                .create("PurchaseOrder")
                                .set("SupplirId", 1));

        assertThrows(
                IllegalStateException.class,
                () -> new SemanticMutationIntentBuilder(model).create("PurchseOrder"));
    }

    @Test
    void mutationDependenciesCannotReferenceAFutureOperation() {
        var model = SupplyChainSemanticModel.build();
        var builder = new SemanticMutationIntentBuilder(model).create("PurchaseOrderLine", "line");

        // The builder deliberately rejects forward references rather than allowing
        // an execution planner/provider to invent an ordering later.
        assertThrows(
                IllegalStateException.class,
                () -> builder.setFrom("PurchaseOrderId", "order", "Id"));
    }

    @Test
    void targetFiltersArePartOfTheSemanticMutationNotProviderText() {
        var model = SupplyChainSemanticModel.build();
        var graph =
                new SemanticMutationIntentBuilder(model)
                        .update("PurchaseOrder")
                        .set("Status", "Closed")
                        .where("Id", SemanticFilterOperator.EQ, 42)
                        .returns("Id")
                        .build();

        assertEquals(1, graph.operations().size());
        var operation = graph.operations().get(0);
        var filter = assertInstanceOf(SemanticFieldFilter.class, operation.filter());
        assertEquals(SemanticFilterOperator.EQ, filter.operator());
        assertEquals(42, filter.value());
    }
}
