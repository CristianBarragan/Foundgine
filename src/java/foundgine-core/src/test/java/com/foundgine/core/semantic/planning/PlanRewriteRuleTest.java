package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.*;

class PlanRewriteRuleTest {
    private static SemanticPlan plan(SemanticFilterExpression filter) {
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                List.of(), null, null, List.of(), new SemanticQueryOptions(filter, List.of(), null, null, null),
                null, null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
        return new SemanticPlan(node);
    }

    @Test void countGreaterThanZeroUsesExistsShortCircuit() {
        var before = plan(new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.COUNT,
                null, SemanticAggregateFilterOperator.GT, 0));
        var after = new AggregateCardinalityOptimizationRule().apply(before);
        assertEquals(AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT, after.root().aggregateExecutionStrategy());
        assertEquals(SemanticEquivalenceFingerprint.create(before), SemanticEquivalenceFingerprint.create(after));
    }

    @Test void countEqualZeroUsesEmptyShortCircuit() {
        var before = plan(new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.COUNT,
                null, SemanticAggregateFilterOperator.EQ, 0));
        var after = new AggregateCardinalityOptimizationRule().apply(before);
        assertEquals(AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT, after.root().aggregateExecutionStrategy());
    }

    @Test void countGreaterThanOneIsNotReduced() {
        var before = plan(new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.COUNT,
                null, SemanticAggregateFilterOperator.GT, 1));
        assertSame(before, new AggregateCardinalityOptimizationRule().apply(before));
    }

    @Test void traversalOptimizationUsesRelationshipCardinality() {
        var child = new SemanticPlanNode(2, ExecutionOperation.TRAVERSE, new EntityId(2), List.of(new FieldId(2)),
                new RelationshipId(7), null, List.of(), null, null, RelationshipCardinality.ONE,
                RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
        var before = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                List.of(new FieldId(1)), null, null, List.of(child)));
        var after = new RelationshipTraversalOptimizationRule().apply(before);
        assertEquals(RelationshipTraversalMode.SINGLE_HOP, after.root().children().get(0).traversalMode());
        assertTrue(SemanticEquivalenceProof.create(before, after).isSatisfied());
    }

    @Test void projectionPruningRemovesOnlyDuplicates() {
        var field = new FieldId(3);
        var before = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                List.of(field, new FieldId(4), field), null, null, List.of()));
        var after = new ProjectionPruningRule().apply(before);
        assertEquals(List.of(field, new FieldId(4)), after.root().fields());
    }

    @Test void authorizationCanonicalizationIsIdempotent() {
        var a = AuthorizationPredicate.parameter("a");
        var b = AuthorizationPredicate.parameter("b");
        var predicate = AuthorizationPredicate.and(AuthorizationPredicate.and(a, b), a);
        var before = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                List.of(), null, null, List.of(), null, predicate, null,
                RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT));
        var rule = new AuthorizationCanonicalizationRule();
        var once = rule.apply(before);
        var twice = rule.apply(once);
        assertEquals(once, twice);
        assertEquals(SemanticEquivalenceFingerprint.create(before), SemanticEquivalenceFingerprint.create(once));
    }
}
