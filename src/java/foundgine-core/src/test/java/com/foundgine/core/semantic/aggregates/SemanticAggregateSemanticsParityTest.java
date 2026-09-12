package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Optional;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Parity port of C# SemanticAggregateSemanticsTests.
 *
 * <p>
 * The Java API uses {@code forAggregate}/{@code tryGet} because {@code for} is
 * a Java keyword. The behavioral contract is otherwise identical: the catalog
 * is closed over the registered aggregates and unknown lookups fail closed.
 */
class SemanticAggregateSemanticsParityTest {

	@Test
	void countReturnsZeroForEmptyCollection() {
		var semantics = SemanticAggregateSemanticsCatalog.forAggregate(SemanticFilterAggregate.COUNT);
		assertEquals(SemanticEmptyCollectionResult.ZERO, semantics.emptyCollectionResult());
	}

	@Test
	void countIsNeverNull() {
		var semantics = SemanticAggregateSemanticsCatalog.forAggregate(SemanticFilterAggregate.COUNT);
		assertEquals(SemanticNullInputBehavior.NEVER_NULL, semantics.nullInputBehavior());
	}

	@Test
	void countIsDuplicateSensitive() {
		var semantics = SemanticAggregateSemanticsCatalog.forAggregate(SemanticFilterAggregate.COUNT);
		assertTrue(semantics.isDuplicateSensitive());
	}

	@Test
	void minReturnsNullForEmptyCollection() {
		var semantics = SemanticAggregateSemanticsCatalog.forAggregate(SemanticFilterAggregate.MIN);
		assertEquals(SemanticEmptyCollectionResult.NULL, semantics.emptyCollectionResult());
	}

	@Test
	void maxReturnsNullForEmptyCollection() {
		var semantics = SemanticAggregateSemanticsCatalog.forAggregate(SemanticFilterAggregate.MAX);
		assertEquals(SemanticEmptyCollectionResult.NULL, semantics.emptyCollectionResult());
	}

	@Test
	void minAndMaxIgnoreNullInput() {
		for (var aggregate : List.of(SemanticFilterAggregate.MIN, SemanticFilterAggregate.MAX)) {
			var semantics = SemanticAggregateSemanticsCatalog.forAggregate(aggregate);
			assertEquals(SemanticNullInputBehavior.IGNORES_NULL, semantics.nullInputBehavior());
		}
	}

	@Test
	void minAndMaxAreDuplicateInsensitive() {
		for (var aggregate : List.of(SemanticFilterAggregate.MIN, SemanticFilterAggregate.MAX)) {
			var semantics = SemanticAggregateSemanticsCatalog.forAggregate(aggregate);
			assertFalse(semantics.isDuplicateSensitive());
		}
	}

	@Test
	void catalogExposesEveryRegisteredAggregate() {
		var aggregates = SemanticAggregateSemanticsCatalog.ALL.stream().map(SemanticAggregateSemantics::aggregate)
				.toList();

		assertTrue(aggregates.contains(SemanticFilterAggregate.COUNT));
		assertTrue(aggregates.contains(SemanticFilterAggregate.MIN));
		assertTrue(aggregates.contains(SemanticFilterAggregate.MAX));
		assertEquals(3, aggregates.size());
	}

	@Test
	void forAggregateFailsClosedForUnregisteredAggregate() {
		assertThrows(UnsupportedOperationException.class, () -> SemanticAggregateSemanticsCatalog.forAggregate(null));
	}

	@Test
	void tryGetReturnsEmptyForUnregisteredAggregate() {
		Optional<SemanticAggregateSemantics> found = SemanticAggregateSemanticsCatalog.tryGet(null);

		assertTrue(found.isEmpty());
	}

	@Test
	void tryGetMatchesForAggregateForRegisteredAggregate() {
		var found = SemanticAggregateSemanticsCatalog.tryGet(SemanticFilterAggregate.COUNT);

		assertTrue(found.isPresent());
		assertEquals(SemanticAggregateSemanticsCatalog.forAggregate(SemanticFilterAggregate.COUNT),
				found.orElseThrow());
	}

	@Test
	void defaultAggregatesDoNotRequireCardinalityProof() {
		assertTrue(SemanticAggregateSemanticsCatalog.ALL.stream()
				.noneMatch(SemanticAggregateSemantics::requiresCardinalityProof));
	}
}
