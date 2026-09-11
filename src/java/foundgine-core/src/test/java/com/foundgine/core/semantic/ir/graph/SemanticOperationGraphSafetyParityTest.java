package com.foundgine.core.semantic.ir.graph;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.ir.SemanticOperation;
import com.foundgine.core.semantic.ir.SemanticReadNode;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the C# graph resource-boundary tests. */
class SemanticOperationGraphSafetyParityTest {
    @Test
    void rejectsExcessiveDepth() {
        var leaf = new SemanticReadNode(5, new EntityId(5), List.of(new FieldId(34)),
                new RelationshipId(24), null, List.of(), null, null);
        var n4 = new SemanticReadNode(4, new EntityId(4), List.of(new FieldId(33)),
                new RelationshipId(23), null, List.of(leaf), null, null);
        var n3 = new SemanticReadNode(3, new EntityId(3), List.of(new FieldId(32)),
                new RelationshipId(22), null, List.of(n4), null, null);
        var n2 = new SemanticReadNode(2, new EntityId(2), List.of(new FieldId(31)),
                new RelationshipId(21), null, List.of(n3), null, null);
        var root = new SemanticReadNode(1, new EntityId(1), List.of(new FieldId(11)),
                null, null, List.of(n2), null, null);

        var graph = SemanticOperationGraph.create(new SemanticOperation(root));
        var ex = assertThrows(IllegalStateException.class, () ->
                SemanticOperationGraphSafetyValidator.validate(graph,
                        new SecurityResourceLimits(32, 256, 3, 255, 512, 256, 32, 256, 64, 16,
                                1000, 1_000_000, 4096, 128, 64, 64, 256, 256)));
        assertTrue(ex.getMessage().toLowerCase().contains("depth"));
    }

    @Test
    void rejectsExcessiveNodes() {
        var a = new SemanticReadNode(2, new EntityId(2), List.of(new FieldId(21)),
                new RelationshipId(12), null, List.of(), null, null);
        var b = new SemanticReadNode(3, new EntityId(3), List.of(new FieldId(31)),
                new RelationshipId(13), null, List.of(), null, null);
        var root = new SemanticReadNode(1, new EntityId(1), List.of(new FieldId(11)),
                null, null, List.of(a, b), null, null);
        var graph = SemanticOperationGraph.create(new SemanticOperation(root));

        var limits = new SecurityResourceLimits(32, 2, 32, 255, 512, 256, 32, 256, 64, 16,
                1000, 1_000_000, 4096, 128, 64, 64, 256, 256);
        assertThrows(IllegalStateException.class,
                () -> SemanticOperationGraphSafetyValidator.validate(graph, limits));
    }

    @Test
    void acceptsBoundedGraph() {
        var child = new SemanticReadNode(2, new EntityId(2), List.of(new FieldId(21)),
                new RelationshipId(12), null, List.of(), null, null);
        var root = new SemanticReadNode(1, new EntityId(1), List.of(new FieldId(11)),
                null, null, List.of(child), null, null);
        var graph = SemanticOperationGraph.create(new SemanticOperation(root));

        var limits = new SecurityResourceLimits(32, 2, 2, 1, 2, 256, 32, 256, 64, 16,
                1000, 1_000_000, 4096, 128, 64, 64, 256, 256);
        assertDoesNotThrow(() -> SemanticOperationGraphSafetyValidator.validate(graph, limits));
    }
}
