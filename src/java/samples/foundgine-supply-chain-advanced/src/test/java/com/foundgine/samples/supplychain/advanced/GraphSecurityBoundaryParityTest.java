package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadIntentCompiler;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;
import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Content-level parity with the C# GraphSecurityBoundaryTests. */
class GraphSecurityBoundaryParityTest {
    @Test
    void deep_bom_traversal_is_rejected_at_the_configured_graph_depth() {
        var intent = new ReadIntent("Product", List.of(
                new ReadSelection("Name"),
                new ReadSelection(null, "components", List.of(
                        new ReadSelection(null, "componentProduct", List.of(
                                new ReadSelection(null, "components", List.of(
                                        new ReadSelection("QuantityPerParent")))))))));
        var compiler = new ReadIntentCompiler(SupplyChainSemanticModel.MODEL);
        var d = SecurityResourceLimits.defaults();
        var tight = new SecurityResourceLimits(d.maxSelectionDepth(), d.maxOperationGraphNodes(), 3,
                d.maxOperationGraphEdges(), d.maxOperationGraphFields(), d.maxSelectionNodes(), d.maxFilterDepth(),
                d.maxFilterNodes(), d.maxOrderTerms(), d.maxOrderPathDepth(), d.maxPageSize(), d.maxOffset(),
                d.maxCursorLength(), d.maxMutationOperations(), d.maxMutationFieldsPerOperation(),
                d.maxMutationReturnFieldsPerOperation(), d.maxMutationDependencies(), d.maxMutationEffects());
        var ex = assertThrows(IllegalStateException.class, () -> compiler.compileOperationGraph(intent, tight));
        assertTrue(ex.getMessage().contains("depth exceeds"));
        assertEquals(4, compiler.compileOperationGraph(intent).nodes().size());
    }

    @Test
    void denied_supplier_incidents_are_pruned_but_authorized_role_keeps_them() {
        var intent = new ReadIntent("Supplier", List.of(
                new ReadSelection("Name"),
                new ReadSelection(null, "incidents", List.of(new ReadSelection("Severity")))));
        var compiler = new ReadIntentCompiler(SupplyChainSemanticModel.MODEL);
        var graph = compiler.compileOperationGraph(intent);
        var contract = SupplyChainSemanticModel.MODEL.freeze().createSnapshot();

        var denied = new SemanticAuthorizer(SupplyChainAuthorization.create("tenant-a", SupplyChainAuthorization.Role.WAREHOUSE_OPERATOR))
                .authorizeGraphWithEvidence(contract, graph);
        assertEquals(1, denied.graph().nodes().size());
        assertEquals(SupplyChainSemanticModel.SUPPLIER, denied.graph().root().entityId());
        assertTrue(denied.graph().nodes().stream().noneMatch(n -> n.entityId().equals(SupplyChainSemanticModel.COMPLIANCE_INCIDENT)));

        var allowed = new SemanticAuthorizer(SupplyChainAuthorization.create("tenant-a", SupplyChainAuthorization.Role.ANALYST))
                .authorizeGraphWithEvidence(contract, graph);
        assertEquals(2, allowed.graph().nodes().size());
        assertTrue(allowed.graph().nodes().stream().anyMatch(n -> n.entityId().equals(SupplyChainSemanticModel.COMPLIANCE_INCIDENT)));
    }
}
