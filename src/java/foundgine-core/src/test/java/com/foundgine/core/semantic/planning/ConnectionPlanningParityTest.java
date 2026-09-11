package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.ir.SemanticOperationCompiler;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Port of {@code ConnectionPlanningTests} (Foundgine.Planning.Tests). */
class ConnectionPlanningParityTest {

    @Test
    void connectionIsPreservedAsDistinctTraversalOperation() {
        var graph = new SemanticGraph();
        var root = graph.addRoot(new EntityId(10), List.of(new FieldId(1)));
        graph.addConnection(new EntityId(20), new ConnectionId(30), root, List.of(new FieldId(2)), null);

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));
        var child = assertEqualsAndReturnSingle(plan.root().children());

        assertEquals(ExecutionOperation.SCAN, plan.root().operation());
        assertEquals(ExecutionOperation.TRAVERSE_CONNECTION, child.operation());
        assertEquals(new ConnectionId(30), child.viaConnection());
        assertNull(child.viaRelationship());
        assertEquals(new EntityId(20), child.entityId());
    }

    @Test
    void connectionCannotBeAttachedToRoot() {
        var graph = new SemanticGraph();
        assertThrows(IllegalArgumentException.class, () ->
            graph.addConnection(new EntityId(20), new ConnectionId(30), null, List.of(new FieldId(2)), null));
    }

    @Test
    void connectionNodeCannotAlsoSpecifyRelationship() {
        var graph = new SemanticGraph();
        var root = graph.addRoot(new EntityId(10));
        var node = graph.addConnection(new EntityId(20), new ConnectionId(30), root);

        assertNull(node.viaRelationship());
        assertEquals(new ConnectionId(30), node.viaConnection());
    }

    @Test
    void authorizationPredicateIsPreservedInExecutionPlan() {
        var predicate = AuthorizationPredicate.equal(
            AuthorizationPredicate.member(AuthorizationPredicate.parameter("user"), "TenantId"),
            AuthorizationPredicate.member(AuthorizationPredicate.parameter("contract"), "TenantId"));

        var graph = new SemanticGraph();
        var root = graph.addRoot(new EntityId(10));
        graph.addConnection(new EntityId(20), new ConnectionId(30), root, List.of(), predicate);

        var plan = new Planner().plan(SemanticOperationCompiler.compile(graph));
        var child = assertEqualsAndReturnSingle(plan.root().children());

        assertEquals(AuthorizationPredicateKind.EQUAL, child.authorization().kind());
        assertEquals("TenantId", child.authorization().left().name());
        assertEquals("TenantId", child.authorization().right().name());
    }

    private static SemanticPlanNode assertEqualsAndReturnSingle(List<SemanticPlanNode> children) {
        assertEquals(1, children.size());
        return children.get(0);
    }
}
