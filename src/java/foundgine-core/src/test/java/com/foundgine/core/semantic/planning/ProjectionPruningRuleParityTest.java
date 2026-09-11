package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Port of {@code ProjectionPruningRuleTests} (Foundgine.Planning.Tests). */
class ProjectionPruningRuleParityTest {

    @Test
    void removesDuplicateProjectionFieldsWithoutReorderingOutput() {
        var plan = createPlan(List.of(new FieldId(1), new FieldId(2), new FieldId(1), new FieldId(3)), null, null);

        var rewritten = new ProjectionPruningRule().apply(plan);

        assertEquals(List.of(new FieldId(1), new FieldId(2), new FieldId(3)), rewritten.root().fields());
    }

    @Test
    void retainsFieldsRequiredByFilter() {
        var filter = new SemanticFieldFilter(new FieldId(7), SemanticFilterOperator.EQ, "active");
        var options = new SemanticQueryOptions(filter, null, null, null, null);
        var plan = createPlan(List.of(new FieldId(1), new FieldId(1)), options, null);

        var rewritten = new ProjectionPruningRule().apply(plan);

        assertTrue(ProjectionPruningRequirements.requiredRootFields(rewritten.root()).contains(new FieldId(7)));
        assertTrue(rewritten.root().fields().contains(new FieldId(1)));
    }

    @Test
    void retainsFieldsRequiredByOrdering() {
        var order = new SemanticOrderTerm(new FieldId(9), SemanticSortDirection.DESC);
        var options = new SemanticQueryOptions(null, List.of(order), null, null, null);
        var plan = createPlan(List.of(new FieldId(1), new FieldId(1)), options, null);

        var rewritten = new ProjectionPruningRule().apply(plan);

        assertTrue(ProjectionPruningRequirements.requiredRootFields(rewritten.root()).contains(new FieldId(9)));
    }

    @Test
    void rewriteIsSemanticallyEquivalent() {
        var plan = createPlan(List.of(new FieldId(1), new FieldId(1)), null, null);
        var rewritten = new ProjectionPruningRule().apply(plan);

        var proof = SemanticEquivalenceProof.create(plan, rewritten);
        assertTrue(proof.isSatisfied());
    }

    @Test
    void rewritePreservesSecurityInvariants() {
        var plan = createPlan(List.of(new FieldId(1), new FieldId(1)), null,
                List.of("tenant.isolation", "authorization.runtime", "visibility.field"));
        var rewritten = new ProjectionPruningRule().apply(plan);

        var proof = SecurityPreservationProof.create(plan, rewritten);
        assertTrue(proof.isSatisfied());
    }

    @Test
    void uniqueRequestedFieldsAreNotPruned() {
        var plan = createPlan(List.of(new FieldId(1), new FieldId(2)), null, null);

        var rewritten = new ProjectionPruningRule().apply(plan);

        assertSame(plan, rewritten);
    }

    private static SemanticPlan createPlan(List<FieldId> fields, SemanticQueryOptions options, List<String> invariants) {
        var root = new SemanticPlanNode(
                1,
                ExecutionOperation.SCAN,
                new EntityId(1),
                fields,
                null,
                null,
                List.of(),
                options,
                null,
                null,
                RelationshipTraversalMode.DEFAULT,
                -1,
                AggregateExecutionStrategy.DEFAULT);
        return new SemanticPlan(root, invariants, null);
    }
}
