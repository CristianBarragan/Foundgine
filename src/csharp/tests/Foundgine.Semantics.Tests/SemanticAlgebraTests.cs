using Foundgine.Core.Semantic.Expressions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticAlgebraTests
{
    [Fact]
    public void LogicalNormalization_FlattensSortsAndDeduplicates()
    {
        var a = new SemanticBinaryExpression("eq",
            new SemanticFieldReferenceExpression(new Foundgine.Core.Abstractions.FieldId(1),
                new SemanticType.Scalar(SemanticScalarKind.Int32)),
            new SemanticLiteralExpression(SemanticValue.From(1), new SemanticType.Scalar(SemanticScalarKind.Int32)),
            SemanticExpressionTypes.Boolean);
        var b = new SemanticBinaryExpression("eq",
            new SemanticFieldReferenceExpression(new Foundgine.Core.Abstractions.FieldId(2),
                new SemanticType.Scalar(SemanticScalarKind.Int32)),
            new SemanticLiteralExpression(SemanticValue.From(2), new SemanticType.Scalar(SemanticScalarKind.Int32)),
            SemanticExpressionTypes.Boolean);
        var expression = new SemanticLogicalExpression(
            SemanticLogicalOperator.And,
            [new SemanticLogicalExpression(SemanticLogicalOperator.And, [b, a]), a]);

        var normalized = SemanticExpressionNormalizer.Normalize(expression);

        var logical = Assert.IsType<SemanticLogicalExpression>(normalized);
        Assert.Equal(2, logical.Operands.Count);
        Assert.Equal(
            SemanticExpressionNormalizer.Canonicalize(logical.Operands[0]),
            SemanticExpressionNormalizer.Canonicalize(logical.Operands
                .OrderBy(SemanticExpressionNormalizer.Canonicalize).First()));
    }

    [Fact]
    public void SemanticValue_IsProviderIndependent()
    {
        var value = SemanticValue.From(42L);
        Assert.Equal(SemanticValueKind.Int64, value.Kind);
        Assert.Equal(42L, value.Value);
    }

    [Fact]
    public void CountOrdering_DoesNotRequireAFieldSemantically()
    {
        var source = new SemanticFieldReferenceExpression(
            new Foundgine.Core.Abstractions.FieldId(7),
            new SemanticType.Collection(new SemanticType.Object("Order")));

        var expression = new SemanticAggregateExpression(SemanticAggregateExpressionKind.Count, source);

        Assert.IsType<SemanticType.Scalar>(expression.ResultType);
        Assert.Equal(SemanticScalarKind.Int64, ((SemanticType.Scalar)expression.ResultType).Kind);
    }
}