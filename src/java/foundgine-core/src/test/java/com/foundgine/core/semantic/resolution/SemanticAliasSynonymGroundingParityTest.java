package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.SemanticModelBuilder;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.SemanticAliasSynonymGroundingTests}.
 *
 * <p>
 * Case study proving that a synonym only grounds to the same interpretation
 * as the canonical word if it survives both hops of the real architecture:
 * declared as an alias on the semantic contract (folded into the lexicon by
 * {@link SemanticLexiconProjection}), and matched by whatever sits behind the
 * candidate source ({@link ISemanticLexicalCandidateSource}). This builds a
 * minimal but real contract (Supplier aliased "Seller", PurchaseOrder aliased
 * "Buys"), projects it with the production {@code SemanticLexiconProjection},
 * and backs {@link SemanticLexicalResolver#ground(String)} with a source that
 * matches a token against either an entry's canonical name or its declared
 * aliases — the same contract the canonical name is matched against.
 */
class SemanticAliasSynonymGroundingParityTest {

	private static final EntityId SUPPLIER = new EntityId(1);
	private static final EntityId PURCHASE_ORDER = new EntityId(2);

	@Test
	void buysGroundsToTheSameInterpretationAsPurchaseOrder() {
		var resolver = buildResolver();

		var canonical = resolver.ground("PurchaseOrder");
		var alias = resolver.ground("buys");

		assertEquals(GroundingOutcome.COMMITTED, canonical.outcome());
		assertEquals(GroundingOutcome.COMMITTED, alias.outcome());

		assertEquals(PURCHASE_ORDER, canonical.committed().rootEntity());
		assertEquals(PURCHASE_ORDER, alias.committed().rootEntity());
		assertEquals(canonical.committed().signature(), alias.committed().signature());
		assertEquals("PurchaseOrder", canonical.committed().steps().get(0).candidate().canonicalName());
		assertEquals("PurchaseOrder", alias.committed().steps().get(0).candidate().canonicalName());
	}

	@Test
	void sellerGroundsToTheSameInterpretationAsSupplier() {
		var resolver = buildResolver();

		var canonical = resolver.ground("supplier");
		var alias = resolver.ground("seller");

		assertEquals(GroundingOutcome.COMMITTED, canonical.outcome());
		assertEquals(GroundingOutcome.COMMITTED, alias.outcome());

		assertEquals(SUPPLIER, canonical.committed().rootEntity());
		assertEquals(SUPPLIER, alias.committed().rootEntity());
		assertEquals(canonical.committed().signature(), alias.committed().signature());
		assertEquals("Supplier", canonical.committed().steps().get(0).candidate().canonicalName());
		assertEquals("Supplier", alias.committed().steps().get(0).candidate().canonicalName());
	}

	@Test
	void fullParaphraseGroundsEveryTokenTheSameWayAsTheCanonicalSentence() {
		var resolver = buildResolver();

		// README: "show me overdue purchase orders from our top supplier in Texas"
		// Paraphrase: "show me overdue buys from our top seller in Texas"
		record Pair(String canonicalWord, String aliasWord, EntityId expectedEntity) {
		}
		for (var pair : List.of(new Pair("purchase order", "buys", PURCHASE_ORDER),
				new Pair("supplier", "seller", SUPPLIER))) {
			var canonical = resolver.ground(pair.canonicalWord());
			var alias = resolver.ground(pair.aliasWord());

			assertEquals(GroundingOutcome.COMMITTED, canonical.outcome());
			assertEquals(GroundingOutcome.COMMITTED, alias.outcome());
			assertEquals(pair.expectedEntity(), canonical.committed().rootEntity());
			assertEquals(pair.expectedEntity(), alias.committed().rootEntity());
			assertEquals(canonical.committed().signature(), alias.committed().signature());
		}
	}

	private static SemanticLexicalResolver buildResolver() {
		var model = new SemanticModelBuilder()
				.entity(SUPPLIER, "Supplier", e -> e.alias("Seller").identity(new FieldId(1), "Id"))
				.entity(PURCHASE_ORDER, "PurchaseOrder", e -> e.alias("Buys").identity(new FieldId(2), "Id")).build()
				.freeze();
		var contract = new SemanticContractSnapshot(model);

		// The production projection is what turns declared aliases into
		// searchable lexicon entries — the same projection a real Elasticsearch
		// or pgvector-backed ISemanticLexicalCandidateSource indexes from.
		var lexicon = SemanticLexiconProjection.build(contract);

		return new SemanticLexicalResolver(contract, new AliasAwareLexicalSource(lexicon));
	}

	/**
	 * Stand-in for a real retrieval provider: matches a token against either an
	 * entry's canonical name or any of its declared aliases, exactly the lookup
	 * an Elasticsearch/pgvector index built from {@code SemanticLexiconProjection}
	 * output performs.
	 */
	private record AliasAwareLexicalSource(List<SemanticLexiconEntry> lexicon) implements ISemanticLexicalCandidateSource {
		@Override
		public List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request) {
			return lexicon.stream().filter(entry -> request.effectiveKinds().contains(entry.kind()))
					.filter(entry -> entry.canonicalName().equalsIgnoreCase(request.token())
							|| entry.effectiveAliases().stream().anyMatch(a -> a.equalsIgnoreCase(request.token())))
					.map(entry -> new SemanticLexicalCandidate(request.token(), entry.kind(), entry.canonicalName(),
							.95, entry.entityId(), entry.relationshipId(), entry.fieldId(), entry.sourceEntityId(),
							entry.targetEntityId(), entry.value(), List.of()))
					.toList();
		}
	}
}