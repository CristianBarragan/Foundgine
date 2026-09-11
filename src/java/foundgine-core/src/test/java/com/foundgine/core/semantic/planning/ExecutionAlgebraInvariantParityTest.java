package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.ir.SemanticOperationCompiler;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;

import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/** Port of {@code ExecutionAlgebraInvariantTests} (Foundgine.Planning.Tests). */
class ExecutionAlgebraInvariantParityTest {

    @Test
    void rootUsesScanAndCarriesQueryClauses() {
        var graph = new SemanticGraph();
        graph.setOptions(new SemanticQueryOptions(null, List.of(), 10, 2, null));
        graph.addRoot(new EntityId(1), List.of(new FieldId(1)));

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));

        assertEquals(ExecutionOperation.SCAN, plan.root().operation());
        assertEquals(10, plan.root().queryOptions().limit().intValue());
        assertEquals(2, plan.root().queryOptions().offset().intValue());
        assertTrue(plan.root().children().isEmpty());
    }

    @Test
    void relationshipTraversalIsLogicalNotProviderSpecific() {
        var graph = new SemanticGraph();
        var root = graph.addRoot(new EntityId(1), List.of(new FieldId(1)));
        graph.add(new EntityId(2), new RelationshipId(7), root, List.of(new FieldId(2)));

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));
        var child = plan.root().children().get(0);

        assertEquals(ExecutionOperation.SCAN, plan.root().operation());
        assertEquals(ExecutionOperation.TRAVERSE, child.operation());
        assertEquals(new RelationshipId(7), child.viaRelationship());
        assertNull(child.viaConnection());
    }

    @Test
    void connectionTraversalIsSeparateFromRelationshipTraversal() {
        var graph = new SemanticGraph();
        var root = graph.addRoot(new EntityId(1), List.of(new FieldId(1)));
        graph.addConnection(new EntityId(2), new ConnectionId(9), root, List.of(new FieldId(2)), null);

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));
        var child = plan.root().children().get(0);

        assertEquals(ExecutionOperation.TRAVERSE_CONNECTION, child.operation());
        assertEquals(new ConnectionId(9), child.viaConnection());
        assertNull(child.viaRelationship());
    }

    @Test
    void authorizationIsPartOfLogicalPlan() {
        var graph = new SemanticGraph();
        var authorization = AuthorizationPredicate.equal(
            AuthorizationPredicate.contextParameter("TenantId"),
            AuthorizationPredicate.constant("42"));
        graph.addRoot(new EntityId(1), List.of(new FieldId(1)), authorization);

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));

        assertSame(authorization, plan.root().authorization());
    }

    @Test
    void queryClausesAreNotEncodedAsExecutionOperations() {
        var graph = new SemanticGraph();
        graph.setOptions(new SemanticQueryOptions(
            new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.EQ, 42),
            List.of(), 5, null, null));
        graph.addRoot(new EntityId(1), List.of(new FieldId(1)));

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));

        assertEquals(ExecutionOperation.SCAN, plan.root().operation());
        assertNotNull(plan.root().queryOptions());
        assertNotNull(plan.root().queryOptions().filter());
        assertEquals(5, plan.root().queryOptions().limit().intValue());
    }

    @Test
    void frozenReadAlgebraContainsOnlyStructuralOperations() {
        var operations = ExecutionOperation.values();

        assertEquals(3, operations.length);
        assertTrue(Arrays.asList(operations).contains(ExecutionOperation.SCAN));
        assertTrue(Arrays.asList(operations).contains(ExecutionOperation.TRAVERSE));
        assertTrue(Arrays.asList(operations).contains(ExecutionOperation.TRAVERSE_CONNECTION));
    }
}
