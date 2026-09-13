package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.aggregates.AggregateProviderCapability;
import java.util.Objects;

/**
 * Fail-closed proof for collapsing predicate-bearing COUNT comparisons into
 * relationship quantifiers.
 */
public record AggregateExistenceCollapseProof(SemanticEquivalenceProof semanticEquivalence,
		boolean predicateShapePreserved, boolean providerCapabilitySatisfied, boolean authorizationPreserved) {
	public boolean isSatisfied() {
		return semanticEquivalence.isSatisfied() && predicateShapePreserved && providerCapabilitySatisfied
				&& authorizationPreserved;
	}

	public static AggregateExistenceCollapseProof create(SemanticPlan before, SemanticPlan after,
			AggregateProviderCapability providerCapability, boolean predicateShapePreserved) {
		Objects.requireNonNull(before);
		Objects.requireNonNull(after);
		Objects.requireNonNull(providerCapability);
		var semantic = SemanticEquivalenceProof.create(before, after);
		if (!predicateShapePreserved)
			throw new IllegalStateException(
					"Existence collapse rejected because the COUNT predicate shape was not preserved.");
		if (!providerCapability.supportsRelationshipQuantifiers())
			throw new IllegalStateException("Provider '" + providerCapability.providerName()
					+ "' does not declare support for relationship quantifiers.");
		var authorization = AuthorizationPreservationProof.create(before, after);
		if (!authorization.isSatisfied())
			throw new IllegalStateException(
					"Existence collapse rejected because the source plan's security contract was not preserved: "
							+ String.join(" ", authorization.violations()));
		return new AggregateExistenceCollapseProof(semantic, predicateShapePreserved,
				providerCapability.supportsRelationshipQuantifiers(), authorization.isSatisfied());
	}
}
