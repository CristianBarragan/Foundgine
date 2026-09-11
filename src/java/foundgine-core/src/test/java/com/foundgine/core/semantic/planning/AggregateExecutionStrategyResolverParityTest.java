package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAggregateFilterOperator;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

import java.util.stream.Stream;

import static com.foundgine.core.semantic.query.SemanticAggregateFilterOperator.EQ;
import static com.foundgine.core.semantic.query.SemanticAggregateFilterOperator.GT;
import static com.foundgine.core.semantic.query.SemanticAggregateFilterOperator.GTE;
import static com.foundgine.core.semantic.query.SemanticAggregateFilterOperator.LT;
import static com.foundgine.core.semantic.query.SemanticAggregateFilterOperator.LTE;
import static com.foundgine.core.semantic.query.SemanticAggregateFilterOperator.NEQ;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Port of {@code AggregateExecutionStrategyResolverTests} (Foundgine.Planning.Tests). */
class AggregateExecutionStrategyResolverParityTest {

    private static Stream<Arguments> existsShortCircuitCases() {
        return Stream.of(
                Arguments.of(GT, 0L),
                Arguments.of(GTE, 1L),
                Arguments.of(NEQ, 0L));
    }

    private static Stream<Arguments> emptyShortCircuitCases() {
        return Stream.of(
                Arguments.of(EQ, 0L),
                Arguments.of(LT, 1L),
                Arguments.of(LTE, 0L));
    }

    private static Stream<Arguments> unresolvedCases() {
        return Stream.of(
                Arguments.of(GTE, 0L),
                Arguments.of(GTE, -1L),
                Arguments.of(LT, 0L),
                Arguments.of(LT, -1L),
                Arguments.of(GT, 1L),
                Arguments.of(EQ, 5L),
                Arguments.of(LTE, 3L));
    }

    private static Stream<Arguments> canonicalCases() {
        return Stream.concat(existsShortCircuitCases(), emptyShortCircuitCases());
    }

    @ParameterizedTest
    @MethodSource("existsShortCircuitCases")
    void comparisonsEquivalentToNonEmptyResolveToExistsShortCircuit(SemanticAggregateFilterOperator op, long value) {
        assertEquals(AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT,
                AggregateExecutionStrategyResolver.resolve(op, value));
    }

    @ParameterizedTest
    @MethodSource("emptyShortCircuitCases")
    void comparisonsEquivalentToEmptyResolveToEmptyShortCircuit(SemanticAggregateFilterOperator op, long value) {
        assertEquals(AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT,
                AggregateExecutionStrategyResolver.resolve(op, value));
    }

    @ParameterizedTest
    @MethodSource("unresolvedCases")
    void comparisonsThatDependOnExactCountDoNotResolve(SemanticAggregateFilterOperator op, long value) {
        assertNull(AggregateExecutionStrategyResolver.resolve(op, value));
    }

    @ParameterizedTest
    @MethodSource("canonicalCases")
    void stringIntegralValuesUseTheSameCanonicalResolution(SemanticAggregateFilterOperator op, long value) {
        assertEquals(
                AggregateExecutionStrategyResolver.resolve(op, value),
                AggregateExecutionStrategyResolver.resolve(op, Long.toString(value)));
    }

    @Test
    void nonIntegralValueDoesNotResolve() {
        assertNull(AggregateExecutionStrategyResolver.resolve(GT, "not-a-number"));
        assertNull(AggregateExecutionStrategyResolver.resolve(GT, null));
    }

    @Test
    void eligibleBareCountFilterMatchingNodeStrategyIsEligible() {
        var filter = new SemanticAggregateFilter(
                new RelationshipId(1), SemanticFilterAggregate.COUNT, null, GT, 0L);

        assertTrue(AggregateExecutionStrategyResolver.isEligibleFor(
                filter, AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT));
    }

    @Test
    void filterWithTargetFieldIsNeverEligibleEvenUnderNonDefaultStrategy() {
        var filter = new SemanticAggregateFilter(
                new RelationshipId(1), SemanticFilterAggregate.COUNT, new FieldId(9), GT, 0L);

        assertFalse(AggregateExecutionStrategyResolver.isEligibleFor(
                filter, AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT));
    }

    @Test
    void filterWithNestedPredicateIsNeverEligibleEvenUnderNonDefaultStrategy() {
        var filter = new SemanticAggregateFilter(
                new RelationshipId(1), SemanticFilterAggregate.COUNT, null, GT, 0L,
                new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "x"));

        assertFalse(AggregateExecutionStrategyResolver.isEligibleFor(
                filter, AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT));
    }

    @Test
    void eligibleFilterUnderDefaultNodeStrategyIsNotEligible() {
        var filter = new SemanticAggregateFilter(
                new RelationshipId(1), SemanticFilterAggregate.COUNT, null, GT, 0L);

        assertFalse(AggregateExecutionStrategyResolver.isEligibleFor(
                filter, AggregateExecutionStrategy.DEFAULT));
    }

    @Test
    void filterWhoseOwnComparisonDisagreesWithNodeStrategyIsNotEligible() {
        // Defensive: a node could in principle carry a strategy that this particular filter's
        // own comparison does not itself resolve to. Eligibility is always re-derived from the
        // filter, never assumed from the node alone.
        var filter = new SemanticAggregateFilter(
                new RelationshipId(1), SemanticFilterAggregate.COUNT, null, EQ, 0L); // resolves to EMPTY_SHORT_CIRCUIT

        assertFalse(AggregateExecutionStrategyResolver.isEligibleFor(
                filter, AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT));
    }
}
