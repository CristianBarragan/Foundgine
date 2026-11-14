using System.Linq.Expressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.SupplyChain.Application.Querying;

/// <summary>
/// Semantic lambdas: <c>Filter&lt;SalesOrder&gt;.Eq(o =&gt; o.Status, "Pending")</c> resolves the semantic field id from
/// the property (semantic names are the CLR names). <c>Some / None / AtLeast</c> take a collection navigation and a
/// predicate over its target. Nothing executes here: the result is the core filter IR that the authorizer and
/// planner already handle.
/// </summary>
public static class Filter<T> where T : class
{
    private static readonly string Entity = typeof(T).Name;

    public static SemanticFilterExpression Eq<TValue>(Expression<Func<T, TValue>> field, TValue value) =>
        new SemanticFieldFilter(Field(field), SemanticFilterOperator.Eq, value);

    public static SemanticFilterExpression Neq<TValue>(Expression<Func<T, TValue>> field, TValue value) =>
        new SemanticFieldFilter(Field(field), SemanticFilterOperator.Neq, value);

    public static SemanticFilterExpression In<TValue>(Expression<Func<T, TValue>> field, params TValue[] values) =>
        new SemanticFieldFilter(Field(field), SemanticFilterOperator.In, values.Cast<object?>().ToArray());

    public static SemanticFilterExpression And(params SemanticFilterExpression[] parts) => new SemanticAndFilter(parts);

    public static SemanticFilterExpression Or(params SemanticFilterExpression[] parts) => new SemanticOrFilter(parts);

    /// <summary>At least one related row satisfies <paramref name="predicate"/>.</summary>
    public static SemanticFilterExpression Some<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship,
        SemanticFilterExpression predicate) =>
        new SemanticRelationshipFilter(Relationship(relationship), SemanticRelationshipQuantifier.Some, predicate);

    /// <summary>No related row satisfies <paramref name="predicate"/> (also true when there are none).</summary>
    public static SemanticFilterExpression None<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship,
        SemanticFilterExpression predicate) =>
        new SemanticRelationshipFilter(Relationship(relationship), SemanticRelationshipQuantifier.None, predicate);

    /// <summary>At least <paramref name="count"/> related rows satisfy <paramref name="predicate"/>.</summary>
    public static SemanticFilterExpression AtLeast<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship,
        int count, SemanticFilterExpression predicate) =>
        new SemanticAggregateFilter(Relationship(relationship), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gte, count, predicate);

    internal static FieldId Field<TValue>(Expression<Func<T, TValue>> field) =>
        FieldId.Create(Entity, MemberName(field));

    internal static RelationshipId Relationship<TValue>(Expression<Func<T, TValue>> navigation) =>
        RelationshipId.Create(Entity, MemberName(navigation));

    private static string MemberName<TValue>(Expression<Func<T, TValue>> selector) =>
        selector.Body switch
        {
            MemberExpression { Member.Name: var n } => n,
            UnaryExpression { Operand: MemberExpression { Member.Name: var n } } => n,
            _ => throw new ArgumentException("Select a single property, for example o => o.Status.", nameof(selector))
        };
}
