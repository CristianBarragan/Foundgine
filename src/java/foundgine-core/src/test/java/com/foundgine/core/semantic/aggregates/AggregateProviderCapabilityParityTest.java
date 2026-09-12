package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.AggregateProviderCapabilityTests}.
 */
class AggregateProviderCapabilityParityTest {

	@ParameterizedTest
	@EnumSource(SemanticFilterAggregate.class)
	void genericSqlSupportsEveryCataloguedAggregate(SemanticFilterAggregate aggregate) {
		assertTrue(AggregateProviderCapabilityRegistry.GENERIC_SQL.supports(aggregate));
	}

	@Test
	void genericSqlDeclaresProviderNameAndPredicateAndQuantifierSupport() {
		var sql = AggregateProviderCapabilityRegistry.GENERIC_SQL;

		assertEquals("sql", sql.providerName());
		assertTrue(sql.supportsAggregatePredicate());
		assertTrue(sql.supportsRelationshipQuantifiers());
	}

	@Test
	void aProviderWithANarrowerDeclaredSetDoesNotSupportUndeclaredAggregates() {
		var narrow = new AggregateProviderCapability("graphql-experimental",
				List.of(SemanticFilterAggregate.COUNT), false, false);

		assertTrue(narrow.supports(SemanticFilterAggregate.COUNT));
		assertFalse(narrow.supports(SemanticFilterAggregate.MIN));
		assertFalse(narrow.supports(SemanticFilterAggregate.MAX));
	}

	@Test
	void aProviderWithNoDeclaredAggregatesSupportsNothing() {
		var empty = new AggregateProviderCapability("static-cache", List.of(), false, false);

		assertFalse(empty.supports(SemanticFilterAggregate.COUNT));
		assertFalse(empty.supports(SemanticFilterAggregate.MIN));
		assertFalse(empty.supports(SemanticFilterAggregate.MAX));
	}
}
