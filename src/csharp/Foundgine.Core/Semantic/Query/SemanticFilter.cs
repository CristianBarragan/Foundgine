using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Expressions;

namespace Foundgine.Core.Semantic.Query;

public abstract record SemanticFilterExpression : SemanticExpression
{
    public override SemanticType ResultType => SemanticExpressionTypes.Boolean;
}

public sealed record SemanticFieldFilter(
    FieldId Field,
    SemanticFilterOperator Operator,
    object? Value) : SemanticFilterExpression
{
    public SemanticValue SemanticValue => global::Foundgine.Core.Semantic.SemanticValue.From(Value);
}

public sealed record SemanticRelationshipFilter(
    RelationshipId Relationship,
    SemanticRelationshipQuantifier Quantifier,
    SemanticFilterExpression Predicate) : SemanticFilterExpression;

/// <summary>
/// Filters a collection relationship by an aggregate over its target rows.
/// The semantic layer describes the aggregate; providers decide how to render it.
/// </summary>
public sealed record SemanticAggregateFilter(
    RelationshipId Relationship,
    SemanticFilterAggregate Aggregate,
    FieldId? Field,
    SemanticAggregateFilterOperator Operator,
    object? Value,
    SemanticFilterExpression? Predicate = null) : SemanticFilterExpression
{
    public SemanticValue SemanticValue => global::Foundgine.Core.Semantic.SemanticValue.From(Value);
}

public sealed record SemanticAndFilter(
    IReadOnlyList<SemanticFilterExpression> Expressions) : SemanticFilterExpression;

public sealed record SemanticOrFilter(
    IReadOnlyList<SemanticFilterExpression> Expressions) : SemanticFilterExpression;

public enum SemanticFilterOperator : byte
{
    Eq,
    Neq,
    In
}

public enum SemanticRelationshipQuantifier : byte
{
    Some,
    None,
    All
}

public enum SemanticFilterAggregate : byte
{
    Count,
    Min,
    Max
}

public enum SemanticAggregateFilterOperator : byte
{
    Eq,
    Neq,
    Gt,
    Gte,
    Lt,
    Lte
}