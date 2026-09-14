package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadIntentCompiler;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.foundgine.core.semantic.mutation.SemanticMutationIntentBuilder;
import com.foundgine.core.semantic.mutation.SemanticMutationPlanner;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import org.junit.jupiter.api.Test;

import java.time.LocalDate;
import java.util.List;

/**
 * Java parity for the C# advanced sample's {@code Semantic/Tests/OpenIntentSupplyChainTests.cs} —
 * the Open Intent (read + mutation) surface exercised directly against the real Supply Chain
 * semantic model, independent of any adapter (JSON/MCP/GraphQL).
 */
class OpenIntentSupplyChainParityTest {

    @Test
    void productShipmentsIsAnOpenLogicalTraversalOverTheRealSupplyChainPath() {
        var model = SupplyChainSemanticModel.MODEL;
        var request =
                new ReadIntent(
                        "Product",
                        List.of(
                                new ReadSelection(
                                        null, "shipments", List.of(new ReadSelection("Status")))));

        var semanticRequest = new ReadIntentCompiler(model).compile(request);
        var graph =
                new SemanticRequestResolver(new SemanticContractSnapshot(model.freeze()))
                        .resolve(semanticRequest);

        var nodes = List.copyOf(graph.nodes());
        assertEquals(4, nodes.size());
        assertEquals("Product", model.get(nodes.get(0).entityId()).name());
        assertEquals("PurchaseOrderLine", model.get(nodes.get(1).entityId()).name());
        assertEquals("PurchaseOrder", model.get(nodes.get(2).entityId()).name());
        assertEquals("Shipment", model.get(nodes.get(3).entityId()).name());
        var status =
                model.get(nodes.get(3).entityId()).fields().stream()
                        .filter(f -> f.name().equals("Status"))
                        .findFirst()
                        .orElseThrow();
        assertTrue(nodes.get(3).fields().contains(status.id()));
    }

    @Test
    void openSupplyChainMutationCoversGeneratedIdentityValueFlowAndBranching() {
        var model = SupplyChainSemanticModel.MODEL;
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
                        .set("Quantity", 25)
                        .returns("Id", "PurchaseOrderId")
                        .create("Shipment", "shipment")
                        .setFrom("PurchaseOrderId", "order", "Id")
                        .set("ExpectedArrival", LocalDate.of(2026, 9, 5))
                        .set("Status", "Planned")
                        .set("Quantity", 25)
                        .returns("Id", "PurchaseOrderId")
                        .update("PurchaseOrder")
                        .set("Status", "Open")
                        .where("Id", SemanticFilterOperator.EQ, 1)
                        .returns("Id")
                        .build();

        var plan = new SemanticMutationPlanner().plan(graph);

        assertEquals(4, plan.operations().size());
        assertEquals(2, plan.dependencies().size());
        var linePurchaseOrderId =
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
        assertTrue(
                plan.dependencies().stream()
                        .anyMatch(
                                d ->
                                        d.toOperationId().equals("1")
                                                && d.targetField().equals(linePurchaseOrderId)));
        assertTrue(
                plan.dependencies().stream()
                        .anyMatch(
                                d ->
                                        d.toOperationId().equals("2")
                                                && d.targetField()
                                                        .equals(shipmentPurchaseOrderId)));
        assertInstanceOf(SemanticFieldFilter.class, plan.operations().get(3).filter());
    }
}
