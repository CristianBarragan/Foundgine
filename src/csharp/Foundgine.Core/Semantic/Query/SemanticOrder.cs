using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Expressions;

namespace Foundgine.Core.Semantic.Query;

/// <summary>Compatibility request shape retained for existing adapters.</summary>
public sealed record SemanticOrderTerm(
    FieldId Field,
    SemanticSortDirection Direction,
    IReadOnlyList<RelationshipId>? Path = null,
    SemanticOrderAggregate Aggregate = SemanticOrderAggregate.None)
{
    public IReadOnlyList<RelationshipId> EffectivePath => Path ?? [];
    public bool IsRootField => EffectivePath.Count == 0;
    public bool IsAggregate => Aggregate != SemanticOrderAggregate.None;

    /// <summary>Maps the compatibility shape into the common expression algebra.</summary>
    public SemanticExpression ToExpression(SemanticExpression source, SemanticType fieldType) =>
        Aggregate switch
        {
            SemanticOrderAggregate.None => EffectivePath.Count == 0
                ? new SemanticFieldReferenceExpression(Field, fieldType)
                : new SemanticPathExpression(source, EffectivePath, fieldType),
            SemanticOrderAggregate.Count => new SemanticAggregateExpression(
                SemanticAggregateExpressionKind.Count,
                new SemanticPathExpression(source, EffectivePath,
                    new SemanticType.Collection(new SemanticType.Object("Target")))),
            SemanticOrderAggregate.Min => new SemanticAggregateExpression(
                SemanticAggregateExpressionKind.Min,
                new SemanticPathExpression(source, EffectivePath,
                    new SemanticType.Collection(new SemanticType.Object("Target"))),
                new SemanticFieldReferenceExpression(Field, fieldType)),
            SemanticOrderAggregate.Max => new SemanticAggregateExpression(
                SemanticAggregateExpressionKind.Max,
                new SemanticPathExpression(source, EffectivePath,
                    new SemanticType.Collection(new SemanticType.Object("Target"))),
                new SemanticFieldReferenceExpression(Field, fieldType)),
            _ => throw new ArgumentOutOfRangeException()
        };
}

/// <summary>General ordering expression. Any semantic expression may be ordered.</summary>
public record SemanticOrderExpression(
    SemanticExpression Expression,
    SemanticSortDirection Direction);

public sealed record SemanticFieldOrderExpression(
    FieldId Field,
    SemanticType Type,
    SemanticSortDirection Direction) : SemanticOrderExpression(
    new SemanticFieldReferenceExpression(Field, Type), Direction);

public sealed record SemanticAggregateOrderExpression(
    SemanticExpression Source,
    SemanticAggregateExpressionKind Aggregate,
    SemanticExpression? Argument,
    SemanticSortDirection Direction) : SemanticOrderExpression(
    new SemanticAggregateExpression(Aggregate, Source, Argument), Direction);

public enum SemanticSortDirection : byte
{
    Asc,
    Desc
}

public enum SemanticOrderAggregate : byte
{
    None,
    Count,
    Min,
    Max
}