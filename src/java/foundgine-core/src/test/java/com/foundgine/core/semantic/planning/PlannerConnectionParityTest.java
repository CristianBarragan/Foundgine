package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.ir.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class PlannerConnectionParityTest {
    @Test
    void connectionChildLowersToConnectionTraversal() {
        var connection = new ConnectionId(9);
        var child = new SemanticReadNode(2, new EntityId(2), List.of(), null, connection, List.of(), null, null);
        var root = new SemanticReadNode(1, new EntityId(1), List.of(), null, null, List.of(child), null, null);
        var plan = new Planner().plan(new SemanticOperation(root));
        assertEquals(ExecutionOperation.SCAN, plan.root().operation());
        assertEquals(ExecutionOperation.TRAVERSE_CONNECTION, plan.root().children().get(0).operation());
        assertEquals(connection, plan.root().children().get(0).viaConnection());
    }

    @Test
    void rootParentEdgeIsRejected() {
        var root = new SemanticReadNode(1, new EntityId(1), List.of(), new RelationshipId(2), null, List.of(), null, null);
        assertThrows(IllegalArgumentException.class, () -> new Planner().plan(new SemanticOperation(root)));
    }
}
