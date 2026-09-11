package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.*;

class AggregateRelationshipFilterPushdownParityTest {
    @Test void someRelationshipPredicateIsPushedIntoCount() {
        var relationship = new RelationshipId(7);
        var childPredicate = new SemanticAggregateFilter(new RelationshipId(99), SemanticFilterAggregate.COUNT,
                null, SemanticAggregateFilterOperator.GT, 0);
        var count = new SemanticAggregateFilter(relationship, SemanticFilterAggregate.COUNT,
                null, SemanticAggregateFilterOperator.GT, 0);
        var some = new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.SOME, childPredicate);
        var filter = new SemanticAndFilter(List.of(count, some));
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null,
                List.of(), new SemanticQueryOptions(filter, List.of(), null, null, null));
        var before = new SemanticPlan(node);
        var after = new AggregateRelationshipFilterPushdownRule().apply(before);
        assertNotSame(before, after);
        assertTrue(after.root().queryOptions().filter() instanceof SemanticAggregateFilter);
        var rewritten = (SemanticAggregateFilter) after.root().queryOptions().filter();
        assertEquals(relationship, rewritten.relationship());
        assertNotNull(rewritten.predicate());
    }

    @Test void unrelatedRelationshipDoesNotRewrite() {
        var count = new SemanticAggregateFilter(new RelationshipId(7), SemanticFilterAggregate.COUNT,
                null, SemanticAggregateFilterOperator.GT, 0);
        var some = new SemanticRelationshipFilter(new RelationshipId(8), SemanticRelationshipQuantifier.SOME,
                new SemanticAggregateFilter(new RelationshipId(99), SemanticFilterAggregate.COUNT, null,
                        SemanticAggregateFilterOperator.GT, 0));
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null,
                List.of(), new SemanticQueryOptions(new SemanticAndFilter(List.of(count, some)), List.of(), null, null, null));
        var before = new SemanticPlan(node);
        assertSame(before, new AggregateRelationshipFilterPushdownRule().apply(before));
    }
}
