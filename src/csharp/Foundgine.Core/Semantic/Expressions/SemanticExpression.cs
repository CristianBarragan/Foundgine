using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Expressions;

/// <summary>
/// Common semantic expression calculus. Expressions describe meaning only;
/// they contain no SQL, GraphQL, EF, provider, or storage instructions.
/// </summary>
public abstract record SemanticExpression
{
    public abstract SemanticType ResultType { get; }
}

public sealed record SemanticLiteralExpression(SemanticValue Value, SemanticType Type) : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

public sealed record SemanticFieldReferenceExpression(FieldId Field, SemanticType Type) : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

public sealed record SemanticRelationshipReferenceExpression(RelationshipId Relationship, SemanticType Type)
    : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

/// <summary>A typed relationship path from a root expression to a target value.</summary>
public sealed record SemanticPathExpression(
    SemanticExpression Source,
    IReadOnlyList<RelationshipId> Relationships,
    SemanticType Type) : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

public sealed record SemanticUnaryExpression(string Operator, SemanticExpression Operand, SemanticType Type)
    : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

public sealed record SemanticBinaryExpression(
    string Operator,
    SemanticExpression Left,
    SemanticExpression Right,
    SemanticType Type) : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

public sealed record SemanticLogicalExpression(
    SemanticLogicalOperator Operator,
    IReadOnlyList<SemanticExpression> Operands) : SemanticExpression
{
    public override SemanticType ResultType => new SemanticType.Scalar(SemanticScalarKind.Boolean);
}

public sealed record SemanticAggregateExpression(
    SemanticAggregateExpressionKind Aggregate,
    SemanticExpression Source,
    SemanticExpression? Argument = null) : SemanticExpression
{
    public override SemanticType ResultType => Aggregate switch
    {
        SemanticAggregateExpressionKind.Count => new SemanticType.Scalar(SemanticScalarKind.Int64),
        SemanticAggregateExpressionKind.Min or SemanticAggregateExpressionKind.Max
            or SemanticAggregateExpressionKind.Sum or SemanticAggregateExpressionKind.Average
            => Argument?.ResultType ?? Source.ResultType,
        _ => throw new ArgumentOutOfRangeException()
    };
}

public sealed record SemanticFunctionExpression(
    string Name,
    IReadOnlyList<SemanticExpression> Arguments,
    SemanticType Type) : SemanticExpression
{
    public override SemanticType ResultType => Type;
}

public enum SemanticLogicalOperator : byte
{
    And,
    Or
}

public enum SemanticAggregateExpressionKind : byte
{
    Count,
    Min,
    Max,
    Sum,
    Average
}

public static class SemanticExpressionTypes
{
    public static SemanticType Boolean { get; } = new SemanticType.Scalar(SemanticScalarKind.Boolean);
    public static SemanticType Int64 { get; } = new SemanticType.Scalar(SemanticScalarKind.Int64);
}