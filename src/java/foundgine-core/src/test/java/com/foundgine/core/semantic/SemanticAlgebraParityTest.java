package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.expressions.SemanticExpression;
import com.foundgine.core.semantic.expressions.SemanticExpressionNormalizer;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Behavioral parity tests for the provider-independent semantic algebra. */
class SemanticAlgebraParityTest {
    private static final SemanticType INT32 = new SemanticType.Scalar(SemanticScalarKind.INT32);

    @Test
    void logicalNormalizationFlattensSortsAndDeduplicates() {
        var a = new SemanticExpression.Binary("eq",
                new SemanticExpression.FieldReference(new FieldId(1), INT32),
                new SemanticExpression.Literal(SemanticValue.from(1), INT32),
                SemanticExpression.SemanticExpressionTypes.BOOLEAN);
        var b = new SemanticExpression.Binary("eq",
                new SemanticExpression.FieldReference(new FieldId(2), INT32),
                new SemanticExpression.Literal(SemanticValue.from(2), INT32),
                SemanticExpression.SemanticExpressionTypes.BOOLEAN);

        var expression = new SemanticExpression.Logical(
                SemanticExpression.LogicalOperator.AND,
                java.util.List.of(
                        new SemanticExpression.Logical(SemanticExpression.LogicalOperator.AND, java.util.List.of(b, a)),
                        a));

        var normalized = assertInstanceOf(SemanticExpression.Logical.class,
                SemanticExpressionNormalizer.normalize(expression));

        assertEquals(2, normalized.operands().size());
        assertEquals(
                SemanticExpressionNormalizer.canonicalize(normalized.operands().get(0)),
                normalized.operands().stream()
                        .map(SemanticExpressionNormalizer::canonicalize)
                        .sorted()
                        .findFirst().orElseThrow());
    }

    @Test
    void semanticValueIsProviderIndependent() {
        var value = SemanticValue.from(42L);
        assertEquals(SemanticValueKind.INT64, value.kind());
        assertEquals(42L, value.value());
    }

    @Test
    void countDoesNotRequireAFieldArgument() {
        var source = new SemanticExpression.FieldReference(
                new FieldId(7),
                new SemanticType.CollectionType(new SemanticType.ObjectType("Order")));

        var expression = new SemanticExpression.Aggregate(
                SemanticExpression.AggregateExpressionKind.COUNT, source, null);

        assertEquals(SemanticScalarKind.INT64,
                assertInstanceOf(SemanticType.Scalar.class, expression.resultType()).kind());
    }
}
