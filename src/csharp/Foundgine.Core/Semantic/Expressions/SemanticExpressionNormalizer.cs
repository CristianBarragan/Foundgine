using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Semantic.Expressions;

/// <summary>
/// Performs only semantics-preserving normalization. It is intentionally
/// conservative: no provider-specific rewrite is performed here.
/// </summary>
public static class SemanticExpressionNormalizer
{
    public static SemanticExpression Normalize(SemanticExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression switch
        {
            SemanticLogicalExpression logical => NormalizeLogical(logical),
            SemanticUnaryExpression unary => unary with { Operand = Normalize(unary.Operand) },
            SemanticBinaryExpression binary => binary with
            {
                Left = Normalize(binary.Left), Right = Normalize(binary.Right)
            },
            SemanticPathExpression path => path with { Source = Normalize(path.Source) },
            SemanticAggregateExpression aggregate => aggregate with
            {
                Source = Normalize(aggregate.Source),
                Argument = aggregate.Argument is null ? null : Normalize(aggregate.Argument)
            },
            SemanticFunctionExpression function => function with
            {
                Arguments = function.Arguments.Select(Normalize).ToArray()
            },
            _ => expression
        };
    }

    public static string Canonicalize(SemanticExpression expression) => Write(Normalize(expression));

    public static string Hash(SemanticExpression expression)
    {
        var bytes = Encoding.UTF8.GetBytes(Canonicalize(expression));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static SemanticExpression NormalizeLogical(SemanticLogicalExpression expression)
    {
        var children = expression.Operands
            .Select(Normalize)
            .SelectMany(x => x is SemanticLogicalExpression nested && nested.Operator == expression.Operator
                ? nested.Operands
                : [x])
            .Where(x => !IsIdentity(expression.Operator, x))
            .Distinct()
            .OrderBy(Write, StringComparer.Ordinal)
            .ToArray();

        if (children.Length == 0)
            return new SemanticLiteralExpression(
                SemanticValue.From(expression.Operator == SemanticLogicalOperator.And),
                SemanticExpressionTypes.Boolean);
        if (children.Length == 1) return children[0];
        return expression with { Operands = children };
    }

    private static bool IsIdentity(SemanticLogicalOperator op, SemanticExpression expression) =>
        expression is SemanticLiteralExpression literal &&
        literal.Value.Kind == SemanticValueKind.Boolean &&
        literal.Value.Value is bool value &&
        ((op == SemanticLogicalOperator.And && value) || (op == SemanticLogicalOperator.Or && !value));

    private static string Write(SemanticExpression expression) => expression switch
    {
        SemanticLiteralExpression x => $"lit:{x.Value.Kind}:{x.Value}",
        SemanticFieldReferenceExpression x => $"field:{x.Field.Value}:{x.Type}",
        SemanticRelationshipReferenceExpression x => $"rel:{x.Relationship.Value}:{x.Type}",
        SemanticUnaryExpression x => $"unary:{x.Operator}({Write(x.Operand)})",
        SemanticBinaryExpression x => $"binary:{x.Operator}({Write(x.Left)},{Write(x.Right)})",
        SemanticLogicalExpression x => $"logical:{x.Operator}({string.Join(',', x.Operands.Select(Write))})",
        SemanticAggregateExpression x =>
            $"aggregate:{x.Aggregate}({Write(x.Source)},{(x.Argument is null ? "" : Write(x.Argument))})",
        SemanticFunctionExpression x => $"function:{x.Name}({string.Join(',', x.Arguments.Select(Write))}):{x.Type}",
        _ => throw new InvalidOperationException($"Unsupported semantic expression '{expression.GetType().Name}'.")
    };
}