package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class SemanticLexicalResolverParityTest {
	private static final EntityId PRODUCT = EntityId.create("Product");

	private SemanticContractSnapshot contract() {
		var id = FieldId.create("Product", "Id");
		var model = new SemanticModelBuilder()
				.entity(PRODUCT, "Product", e -> e.identity(id, "Id").field(id, "Id", String.class)).build().freeze();
		return new SemanticContractSnapshot(model);
	}

	private ISemanticLexicalCandidateSource source(List<SemanticLexicalCandidate> candidates) {
		return request -> candidates.stream().filter(x -> x.token().equalsIgnoreCase(request.token())).toList();
	}

	@Test
	void tooManyTokensFailBeforeRetrieval() {
		var resolver = new SemanticLexicalResolver(contract(), request -> {
			fail("retrieval must not occur when token budget is exceeded");
			return List.of();
		}, 20, 4, .03, 2, 100, 1000, 100, null);
		var decision = resolver.ground("one two three");
		assertEquals(GroundingOutcome.BUDGET_EXCEEDED, decision.outcome());
		assertEquals(GroundingBudgetLimit.MAX_TOKENS, decision.budgetLimit());
	}

	@Test
	void competingSemanticMeaningsRequireClarification() {
		var first = new SemanticLexicalCandidate("name", SemanticLexicalCandidateKind.FIELD, "Name", .91, PRODUCT, null,
				FieldId.create("Product", "Name"), PRODUCT, PRODUCT, null, List.of());
		var second = new SemanticLexicalCandidate("name", SemanticLexicalCandidateKind.FIELD, "DisplayName", .90,
				PRODUCT, null, FieldId.create("Product", "DisplayName"), PRODUCT, PRODUCT, null, List.of());
		var resolver = new SemanticLexicalResolver(contract(), source(List.of(first, second)), 20, 4, .03, 32, 5000,
				1000, 100, null);
		var decision = resolver.ground("name");
		assertEquals(GroundingOutcome.REQUIRES_CLARIFICATION, decision.outcome());
		assertNull(decision.committed());
	}

	@Test
	void cancelledRetrievalFailsClosed() {
		var resolver = new SemanticLexicalResolver(contract(),
				request -> List.of(new SemanticLexicalCandidate(request.token(), SemanticLexicalCandidateKind.ENTITY,
						"Product", .9, PRODUCT, null, null, PRODUCT, PRODUCT, null, List.of())));
		var decision = resolver.ground("product", () -> true);
		assertEquals(GroundingOutcome.BUDGET_EXCEEDED, decision.outcome());
		assertEquals(GroundingBudgetLimit.CANCELLED, decision.budgetLimit());
	}
}
