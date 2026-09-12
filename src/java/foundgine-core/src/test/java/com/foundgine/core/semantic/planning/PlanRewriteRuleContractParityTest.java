package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class PlanRewriteRuleContractParityTest {
	private static SemanticPlan plan(AuthorizationPredicate authorization) {
		return new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
				List.of(new FieldId(1)), null, null, List.of(), null, authorization, null,
				RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT), List.of(), null);
	}

	@Test
	void authorizationRuleExposesAuditableContract() {
		var rule = new AuthorizationCanonicalizationRule();
		assertEquals("authorization.canonicalization", rule.name());
		assertFalse(rule.preconditions().isEmpty());
		assertTrue(rule.securityObligations().contains("authorization.required"));
		assertTrue(rule.securityObligations().contains("authorization.runtime"));
		assertEquals(0d, rule.costImpact());
	}

	@Test
	void ruleApplicationProducesProofs() {
		var predicate = AuthorizationPredicate.and(
				AuthorizationPredicate.equal(
						AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
						AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId")),
				AuthorizationPredicate.equal(
						AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "Region"),
						AuthorizationPredicate.constant("NZ")));
		var before = plan(predicate);
		var rule = new AuthorizationCanonicalizationRule();
		var result = SemanticPlanOptimizer.applyRule(rule, before, rule.apply(before));
		assertTrue(result.isSatisfied());
		assertTrue(result.securityProof().isSatisfied());
		assertTrue(result.semanticProof().isSatisfied());
	}

	@Test
	void optimizerUsesRuleContractAndRecordsRuleName() {
		var predicate = AuthorizationPredicate.not(AuthorizationPredicate.not(AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"))));
		var result = new SemanticPlanOptimizer().optimize(plan(predicate));
		assertTrue(result.appliedRules().contains("authorization.canonicalization"));
		assertTrue(result.securityProof().isSatisfied());
		assertTrue(result.semanticProof().isSatisfied());
	}

	@Test
	void ruleCannotApplyWithoutPrecondition() {
		var rule = new AuthorizationCanonicalizationRule();
		var before = plan(null);
		assertFalse(rule.canApply(before));
		assertSame(before, rule.apply(before));
	}

	@Test
	void unknownSecurityObligationFailsClosed() {
		var rule = new IPlanRewriteRule() {
			public String name() {
				return "test.unknown-obligation";
			}

			public List<String> preconditions() {
				return List.of();
			}

			public List<String> securityObligations() {
				return List.of("security.this-does-not-exist");
			}

			public double costImpact() {
				return 0d;
			}

			public boolean canApply(SemanticPlan p) {
				return true;
			}

			public SemanticPlan apply(SemanticPlan p) {
				return p;
			}
		};
		var before = SecurityInvariantPlanRequirements.attach(plan(null));
		assertThrows(IllegalStateException.class, () -> SemanticPlanOptimizer.applyRule(rule, before, before));
	}
}
