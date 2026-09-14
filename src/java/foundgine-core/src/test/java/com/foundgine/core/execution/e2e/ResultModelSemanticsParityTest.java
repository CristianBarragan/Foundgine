package com.foundgine.core.execution.e2e;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionCellKey;
import com.foundgine.core.execution.ExecutionEvidence;
import com.foundgine.core.execution.ExecutionPageInfo;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.execution.ResultMaterializer;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import com.foundgine.core.semantic.results.SemanticResultPageInfo;

import org.junit.jupiter.api.Test;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Port of C# {@code Foundgine.E2E.Tests.ResultModelSemanticsTests}.
 *
 * <p><b>Porting decisions:</b>
 *
 * <ul>
 *   <li>The C# original's first case ({@code
 *       Materializer_returns_the_canonical_semantic_result_type}) asserts {@code
 *       Assert.IsType<SemanticResult>(result)} - a runtime type check that only has meaning in C#
 *       because {@code ResultMaterializer.Materialize} could in principle be widened to return a
 *       base type or interface. The Java port's {@code ResultMaterializer.materialize} signature
 *       already declares a return type of {@code SemanticResult} (a Java {@code record}, not
 *       subclassable), so the type is a compile-time guarantee here and a dedicated
 *       runtime-type-check test would be vacuous. That case is not ported; the other two, which
 *       assert genuine behavioral contracts, are.
 *   <li>The existing {@code ResultMaterializationParityE2ETest} in this package covers
 *       row-collapsing and null-root/child-identity handling, but not the two contracts ported
 *       here: that identity fields are not required to appear in the field projection to
 *       reconstruct distinct nodes, and that page info / evidence survive materialization
 *       unchanged.
 * </ul>
 */
class ResultModelSemanticsParityTest {

    private static final EntityId CUSTOMER = new EntityId(1);
    private static final FieldId ID = new FieldId(1);
    private static final FieldId NAME = new FieldId(2);

    private static SemanticModel modelWithNameOnly() {
        return new SemanticModelBuilder()
                .entity(
                        CUSTOMER,
                        "Customer",
                        e ->
                                e.identity(ID, "Id")
                                        .field(ID, "Id", Long.class)
                                        .field(NAME, "Name", String.class))
                .build();
    }

    @Test
    void identityIsNotRequiredInTheProjectionToReconstructUniqueNodes() {
        var model = modelWithNameOnly();
        var plan =
                new SemanticPlan(
                        new SemanticPlanNode(
                                1,
                                ExecutionOperation.SCAN,
                                CUSTOMER,
                                List.of(NAME),
                                null,
                                null,
                                List.of()));

        ExecutionRow row = row(1L, "Ada");
        ExecutionRow row2 = row(2L, "Grace");
        ExecutionRow row3 = row(1L, "Ada");

        var result =
                new ResultMaterializer(model)
                        .materialize(plan, new ExecutionResult(List.of(row, row2, row3)));

        assertEquals(2, result.roots().size());
        assertEquals(1L, result.roots().get(0).identityValue());
        assertEquals(2L, result.roots().get(1).identityValue());
        assertFalse(result.roots().get(0).values().containsKey(ID));
        assertEquals("Ada", result.roots().get(0).values().get(NAME));
    }

    @Test
    void materializationPreservesExecutionPageInfoAndEvidence() {
        var model = modelWithNameOnly();
        var plan =
                new SemanticPlan(
                        new SemanticPlanNode(
                                1,
                                ExecutionOperation.SCAN,
                                CUSTOMER,
                                List.of(ID),
                                null,
                                null,
                                List.of()));

        var pageInfo = new ExecutionPageInfo("start", "end", true, false);
        var evidence = new ExecutionEvidence("sql", "plan", List.of(1), 1, 3);
        ExecutionRow row = row(42L, null);

        var materialized =
                new ResultMaterializer(model)
                        .materialize(
                                plan, new ExecutionResult(List.of(row), pageInfo, evidence, null));

        assertEquals(
                new SemanticResultPageInfo("start", "end", true, false), materialized.pageInfo());
        assertNotNull(materialized.evidence());
        assertEquals("sql", materialized.evidence().provider());
        assertEquals("plan", materialized.evidence().planFingerprint());
        assertEquals(List.of(1), materialized.evidence().authorizedNodeIds());
        assertEquals(1, materialized.evidence().rowsReturned());
        assertEquals(3, materialized.evidence().elapsedMilliseconds());
        assertNull(materialized.evidence().providerOperationFingerprint());
        assertNull(materialized.evidence().intentFingerprint());
        assertNull(materialized.evidence().authorizationFingerprint());
    }

    private static ExecutionRow row(long identity, String name) {
        Map<ExecutionCellKey, Object> cells = new LinkedHashMap<>();
        cells.put(new ExecutionCellKey(1, CUSTOMER, ID), identity);
        if (name != null) {
            cells.put(new ExecutionCellKey(1, CUSTOMER, NAME), name);
        }
        return new ExecutionRow(Map.of(), cells);
    }
}
