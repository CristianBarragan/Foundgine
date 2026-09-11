package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertTrue;

/** Port of {@code RewriteRuleComposerRegressionTests} (Foundgine.Planning.Tests). */
class RewriteRuleComposerRegressionParityTest {

    @Test
    void equivalentRebuiltPlanIsNotReportedAsACycle() {
        var plan = new SemanticPlan(new SemanticPlanNode(
                1,
                ExecutionOperation.SCAN,
                new EntityId(1),
                List.of(new FieldId(1)),
                null,
                null,
                List.of()));

        var composer = new RewriteRuleComposer(List.of(new EquivalentRebuildRule()));

        var result = composer.compose(plan);

        assertTrue(result.terminatedNormally());
        assertTrue(result.applications().isEmpty());
    }

    /** Mirrors the C# nested {@code EquivalentRebuildRule}: rebuilds an identical plan each pass. */
    private static final class EquivalentRebuildRule implements IPlanRewriteRule {
        @Override public String name() { return "test.equivalent-rebuild"; }
        @Override public List<String> preconditions() { return List.of(); }
        @Override public List<String> securityObligations() { return List.of(); }
        @Override public double costImpact() { return 0; }
        @Override public double benefitEstimate() { return 1; }
        @Override public boolean isIdempotent() { return true; }
        @Override public int priority() { return 0; }
        @Override public boolean canApply(SemanticPlan plan) { return true; }

        @Override
        public SemanticPlan apply(SemanticPlan plan) {
            var root = plan.root();
            var rebuilt = new SemanticPlanNode(root.id(), root.operation(), root.entityId(),
                    List.copyOf(root.fields()), root.viaRelationship(), root.viaConnection(),
                    root.children(), root.queryOptions(), root.authorization(),
                    root.relationshipCardinality(), root.traversalMode(), root.traversalOrder(),
                    root.aggregateExecutionStrategy());
            return new SemanticPlan(rebuilt, plan.requiredSecurityInvariants(), plan.authorizationBinding());
        }
    }
}
