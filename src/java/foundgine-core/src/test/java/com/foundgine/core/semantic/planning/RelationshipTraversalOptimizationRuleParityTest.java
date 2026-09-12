package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of {@code RelationshipTraversalOptimizationRuleTests}
 * (Foundgine.Planning.Tests).
 */
class RelationshipTraversalOptimizationRuleParityTest {

    @Test
    void oneRelationshipBecomesSingleHop() {
        var child = node(2, RelationshipCardinality.ONE);
        var plan = plan(child);

        var optimized = new RelationshipTraversalOptimizationRule().apply(plan);

        assertEquals(RelationshipTraversalMode.SINGLE_HOP, optimized.root().children().get(0).traversalMode());
        assertEquals(RelationshipCardinality.ONE, optimized.root().children().get(0).relationshipCardinality());
    }

    @Test
    void manyRelationshipBecomesSetBased() {
        var child = node(2, RelationshipCardinality.MANY);
        var plan = plan(child);

        var optimized = new RelationshipTraversalOptimizationRule().apply(plan);

        assertEquals(RelationshipTraversalMode.SET_BASED, optimized.root().children().get(0).traversalMode());
    }

    @Test
    void missingCardinalityDoesNotRewrite() {
        var child = node(2, null);
        var plan = plan(child);

        var optimized = new RelationshipTraversalOptimizationRule().apply(plan);

        assertSame(plan, optimized);
    }

    @Test
    void traversalHintDoesNotChangeSemanticEquivalence() {
        var before = plan(node(2, RelationshipCardinality.ONE));
        var after = new RelationshipTraversalOptimizationRule().apply(before);

        var proof = SemanticEquivalenceProof.create(before, after);

        assertTrue(proof.isSatisfied());
    }

    private static SemanticPlan plan(SemanticPlanNode child) {
        return new SemanticPlan(new SemanticPlanNode(
                1,
                ExecutionOperation.SCAN,
                new EntityId(1),
                List.of(new FieldId(1)),
                null,
                null,
                List.of(child)));
    }

    private static SemanticPlanNode node(int id, RelationshipCardinality cardinality) {
        return new SemanticPlanNode(
                id,
                ExecutionOperation.TRAVERSE,
                new EntityId(id),
                List.of(new FieldId(id)),
                new RelationshipId(7),
                null,
                List.of(),
                null,
                null,
                cardinality,
                RelationshipTraversalMode.DEFAULT,
                -1,
                AggregateExecutionStrategy.DEFAULT);
    }
}
