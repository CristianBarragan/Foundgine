package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.aggregates.*;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.*;

/**
 * Composite, fail-closed proof gate for substituting one aggregate for another.
 */
public record AggregateRewriteProof(SemanticEquivalenceProof semanticEquivalence,
		AggregateRewriteLegalityResult emptySetEquivalence, AggregateRewriteLegalityResult nullEquivalence,
		AggregateRewriteLegalityResult duplicateEquivalence, AggregateRewriteLegalityResult cardinalityProof,
		SemanticFilterAggregate targetAggregate, AggregateProviderCapability providerCapability,
		ProviderCostEstimate costEstimate, AuthorizationPreservationProof authorizationPreservation) {

	public boolean isSatisfied() {
		return semanticEquivalence.isSatisfied() && emptySetEquivalence.isLegal() && nullEquivalence.isLegal()
				&& duplicateEquivalence.isLegal() && cardinalityProof.isLegal()
				&& providerCapability.supports(targetAggregate) && authorizationPreservation.isSatisfied();
	}

	public static AggregateRewriteProof create(SemanticPlan before, SemanticPlan after, SemanticAggregateSemantics from,
			SemanticAggregateSemantics to, AggregateCardinalityProof cardinalityProof,
			AggregateProviderCapability providerCapability, ProviderCostEstimate costEstimate) {
		Objects.requireNonNull(before);
		Objects.requireNonNull(after);
		Objects.requireNonNull(from);
		Objects.requireNonNull(to);
		Objects.requireNonNull(cardinalityProof);
		Objects.requireNonNull(providerCapability);
		var semantic = SemanticEquivalenceProof.create(before, after);
		if (!providerCapability.supports(to.aggregate()))
			throw new IllegalStateException("Provider '" + providerCapability.providerName()
					+ "' does not declare support for aggregate '" + to.aggregate() + "'.");
		var empty = AggregateRewriteLegality.checkEmptySemantics(from, to);
		var nul = AggregateRewriteLegality.checkNullSemantics(from, to);
		var dup = AggregateRewriteLegality.checkDuplicateSensitivity(from, to);
		var card = AggregateRewriteLegality.checkCardinalityRequirement(from, to, cardinalityProof.knowledge());
		var combined = AggregateRewriteLegalityResult.combine(empty, nul, dup, card);
		if (!combined.isLegal())
			throw new IllegalStateException(
					"Aggregate rewrite rejected because it does not preserve aggregate semantics: "
							+ String.join(" ", combined.violations()));
		var auth = AuthorizationPreservationProof.create(before, after);
		if (!auth.isSatisfied())
			throw new IllegalStateException(
					"Aggregate rewrite rejected because it does not preserve the source plan's security contract: "
							+ String.join(" ", auth.violations()));
		return new AggregateRewriteProof(semantic, empty, nul, dup, card, to.aggregate(), providerCapability,
				costEstimate, auth);
	}
}
