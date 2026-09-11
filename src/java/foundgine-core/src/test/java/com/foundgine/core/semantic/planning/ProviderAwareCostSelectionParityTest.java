package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Port of {@code ProviderAwareCostSelectionTests} (Foundgine.Planning.Tests). */
class ProviderAwareCostSelectionParityTest {

    @Test
    void providerCostCanChangeRuleSelection() {
        var cheap = new SelectionRule("test.cheap", 5d, 0d, false);
        var expensive = new SelectionRule("test.expensive", 8d, 0d, false);
        var estimator = new FakeProviderCostEstimator((before, candidate, rule) ->
            rule.name().equals("test.expensive")
                ? ProviderCostEstimate.from("test-provider", 100d, 10d, 0.9d, null)
                : ProviderCostEstimate.from("test-provider", 1d, 10d, 0.9d, null));

        var selector = new RewriteRuleSelector(null, estimator, new ProviderCostSelectionPolicy(true, 1d));
        var selected = selector.selectProviderAware(plan(), List.of(cheap, expensive));

        assertNotNull(selected);
        assertEquals("test.cheap", selected.ruleName());
        assertEquals("test-provider", selected.provider());
        assertEquals(1d, selected.estimatedExecutionCost());
    }

    @Test
    void providerCostIsAdvisoryAndDoesNotReplaceProofChecks() {
        var invalid = new SelectionRule("test.invalid", 1000d, 0d, true);
        var estimator = new FakeProviderCostEstimator((before, candidate, rule) ->
            ProviderCostEstimate.from("test-provider", 0d, 1d, 1d, null));

        var composer = new RewriteRuleComposer(List.of(invalid),
            new RewriteRuleCompositionOptions(100, 100, null, estimator, null));

        assertThrows(IllegalStateException.class, () -> composer.compose(plan()));
    }

    @Test
    void providerSelectionHistoryContainsEstimateAndScore() {
        var rule = new SelectionRule("test.rule", 4d, 2d, false);
        var estimator = new FakeProviderCostEstimator((before, candidate, selected) ->
            ProviderCostEstimate.from("test-provider", 3d, 12d, 0.75d, null));

        var result = new RewriteRuleComposer(List.of(rule),
            new RewriteRuleCompositionOptions(100, 100, null, estimator, null)).compose(plan());

        var candidate = assertEqualsAndReturnOne(result.providerSelectionHistory());
        assertEquals("test-provider", candidate.provider());
        assertEquals(3d, candidate.estimatedExecutionCost());
        assertEquals(12d, candidate.estimatedRows());
        assertEquals(0.75d, candidate.costConfidence());
        assertTrue(candidate.score() > 0d);
    }

    private static ProviderAwareRewriteRuleCandidate assertEqualsAndReturnOne(
        List<ProviderAwareRewriteRuleCandidate> values) {
        assertEquals(1, values.size());
        return values.get(0);
    }

    private static SemanticPlan plan() {
        var auth = AuthorizationPredicate.equal(
            AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"));
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
            List.of(new FieldId(1)), null, null, List.of(), null, auth,
            null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
        return new SemanticPlan(node);
    }

    @FunctionalInterface
    private interface EstimatorFunction {
        ProviderCostEstimate estimate(SemanticPlan before, SemanticPlan candidate, IPlanRewriteRule rule);
    }

    private static final class FakeProviderCostEstimator implements IProviderCostEstimator {
        private final EstimatorFunction function;
        private FakeProviderCostEstimator(EstimatorFunction function) { this.function = function; }
        @Override public String provider() { return "test-provider"; }
        @Override public ProviderCostEstimate estimate(SemanticPlan before, SemanticPlan candidate,
                                                       IPlanRewriteRule rule) {
            return function.estimate(before, candidate, rule);
        }
    }

    private static final class SelectionRule implements IPlanRewriteRule {
        private final String name;
        private final double benefit;
        private final double cost;
        private final boolean changesMeaning;

        private SelectionRule(String name, double benefit, double cost, boolean changesMeaning) {
            this.name = name; this.benefit = benefit; this.cost = cost; this.changesMeaning = changesMeaning;
        }
        @Override public String name() { return name; }
        @Override public List<String> preconditions() { return List.of("test plan"); }
        @Override public List<String> securityObligations() { return List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED); }
        @Override public double costImpact() { return cost; }
        @Override public double benefitEstimate() { return benefit; }
        @Override public boolean canApply(SemanticPlan plan) { return true; }
        @Override public SemanticPlan apply(SemanticPlan plan) {
            if (!changesMeaning) return plan;
            var root = plan.root();
            return new SemanticPlan(new SemanticPlanNode(root.id(), root.operation(), root.entityId(),
                List.of(new FieldId(99)), root.viaRelationship(), root.viaConnection(), root.children(),
                root.queryOptions(), root.authorization(), root.relationshipCardinality(), root.traversalMode(),
                root.traversalOrder(), root.aggregateExecutionStrategy()), plan.requiredSecurityInvariants(),
                plan.authorizationBinding());
        }
    }
}
