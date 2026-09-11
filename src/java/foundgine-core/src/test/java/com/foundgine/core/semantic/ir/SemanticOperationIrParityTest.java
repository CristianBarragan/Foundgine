package com.foundgine.core.semantic.ir;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Port of {@code SemanticOperationIrTests} (Foundgine.Semantics.Tests). */
class SemanticOperationIrParityTest {

    @Test
    void compilerPreservesSemanticTopologyWithoutProviderInformation() {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var orders = new RelationshipId(10);
        var customerId = new FieldId(11);
        var orderId = new FieldId(21);

        var graph = new SemanticGraph();
        var root = graph.addRoot(customer, List.of(customerId));
        graph.add(order, orders, root, List.of(orderId));

        var operation = SemanticOperationCompiler.compile(graph);

        assertEquals(customer, operation.root().entityId());
        assertEquals(List.of(customerId), operation.root().fields());
        assertEquals(1, operation.root().children().size());

        var child = operation.root().children().get(0);
        assertEquals(order, child.entityId());
        assertEquals(orders, child.viaRelationship());
        assertNull(child.viaConnection());
        assertEquals(List.of(orderId), child.fields());
    }

    @Test
    void compilerPreservesRootQueryOptions() {
        var graph = new SemanticGraph();
        graph.setOptions(new SemanticQueryOptions(null, List.of(), 25, null, null));
        graph.addRoot(new EntityId(1));

        var operation = SemanticOperationCompiler.compile(graph);

        assertNotNull(operation.root().queryOptions());
        assertEquals(Integer.valueOf(25), operation.root().queryOptions().limit());
    }

    @Test
    void compilerPreservesAuthorizationAsSemanticConstraint() {
        var entity = new EntityId(1);
        var authorization = AuthorizationPredicate.equal(
            AuthorizationPredicate.member(AuthorizationPredicate.parameter("customer"), "tenantId"),
            AuthorizationPredicate.contextParameter("tenantId"));

        var graph = new SemanticGraph();
        graph.addRoot(entity, List.of(), authorization);

        var operation = SemanticOperationCompiler.compile(graph);

        assertEquals(authorization, operation.root().authorization());
    }

    @Test
    void compilerRejectsMultipleRoots() {
        var graph = new SemanticGraph();
        graph.addRoot(new EntityId(1));
        graph.addRoot(new EntityId(2));

        var error = assertThrows(IllegalStateException.class, () -> SemanticOperationCompiler.compile(graph));

        assertTrue(error.getMessage().contains("exactly one root"));
    }
}
