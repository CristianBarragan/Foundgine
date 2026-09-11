package com.foundgine.core.execution.e2e;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.semantic.planning.mutation.*;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.*;

class ExecutionMutationIRParityE2ETest {
    @Test
    void mutationPlanLowersToCanonicalProviderNeutralIR() {
        var entity = new MutationEntitySchema(
                EntityId.create("Customer"), "Customer",
                Set.of(ColumnId.create("customers", "name")),
                Map.of(FieldId.create("Customer", "Name"), ColumnId.create("customers", "name")),
                ColumnId.create("customers", "id"));
        var op = new MutationOperation(entity, MutationKind.CREATE,
                List.of(new MutationFieldValue(ColumnId.create("customers", "name"), "Alice")),
                null, null, List.of(FieldId.create("Customer", "Name")));
        var ir = ExecutionMutationIR.ExecutionMutationIRCompiler.compile(new MutationBatchPlan(List.of(op), List.of()));

        assertEquals(1, ir.operations().size());
        assertSame(op, ir.operations().getFirst());
        assertTrue(ir.dependencies().isEmpty());
    }

    @Test
    void forwardDependencyIsRejectedAtExecutionBoundary() {
        var entity = new MutationEntitySchema(
                EntityId.create("Customer"), "Customer", Set.of(), Map.of(), null);
        var op = new MutationOperation(entity, MutationKind.DELETE, List.of(), null, null, List.of());
        var dependency = new MutationDependency(0, 0, FieldId.create("Customer", "Id"), ColumnId.create("customers", "id"));
        assertThrows(IllegalStateException.class,
                () -> ExecutionMutationIR.ExecutionMutationIRCompiler.compile(new MutationBatchPlan(List.of(op), List.of(dependency))));
    }
}
