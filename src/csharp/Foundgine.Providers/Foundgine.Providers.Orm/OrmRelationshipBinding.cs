using System.Linq.Expressions;

namespace Foundgine.Providers.Orm;

/// <summary>Generated, strongly typed collection navigation used to lower semantic relationship quantifiers.</summary>
public interface IOrmRelationshipBinding<TEntity> where TEntity : class
{
    string Name { get; }
    Type TargetType { get; }
    LambdaExpression Build(LambdaExpression predicate, Foundgine.Core.Semantic.Query.SemanticRelationshipQuantifier quantifier);
    LambdaExpression BuildAggregate(LambdaExpression predicate, Foundgine.Core.Semantic.Query.SemanticFilterAggregate aggregate, LambdaExpression? selector, Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator op, object? value);
    LambdaExpression BuildCountAggregateOrderSelector(LambdaExpression predicate);
    LambdaExpression BuildCountAggregateCursorComparison(LambdaExpression predicate, int cursorValue, bool descending, bool equal);
    IOrderedQueryable<TEntity> ApplyAggregateOrder(IQueryable<TEntity> query, LambdaExpression predicate, Foundgine.Core.Semantic.Query.SemanticOrderAggregate aggregate, LambdaExpression? selector, bool descending, bool thenBy);


}

/// <summary>
/// A source-generated navigation binding. It builds Enumerable.Any-based quantifier expression trees; EF Core and
/// The EF Core provider translates these navigation expressions to correlated EXISTS predicates.
/// </summary>
public sealed class OrmRelationshipBinding<TParent, TChild> : IOrmRelationshipBinding<TParent>
    where TParent : class where TChild : class
{
    private static readonly System.Reflection.MethodInfo AnyMethod =
        ((MethodCallExpression)((Expression<Func<IEnumerable<TChild>, Func<TChild, bool>, bool>>)
            ((source, predicate) => source.Any(predicate))).Body).Method;

    public OrmRelationshipBinding(string name, Expression<Func<TParent, IEnumerable<TChild>>> navigation)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Relationship name is required.", nameof(name)) : name;
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }
    

    public string Name { get; }
    public Type TargetType => typeof(TChild);
    public Expression<Func<TParent, IEnumerable<TChild>>> Navigation { get; }

    public LambdaExpression Build(LambdaExpression predicate, Foundgine.Core.Semantic.Query.SemanticRelationshipQuantifier quantifier)
    {
        if (predicate.Parameters.Count != 1 || predicate.Parameters[0].Type != typeof(TChild) || predicate.ReturnType != typeof(bool))
            throw new InvalidOperationException($"Relationship '{Name}' predicate does not target '{typeof(TChild)}'.");
        var parent = Navigation.Parameters[0];
        var childPredicate = predicate;
        var any = Expression.Call(AnyMethod, Navigation.Body, childPredicate);
        Expression body = quantifier switch
        {
            Foundgine.Core.Semantic.Query.SemanticRelationshipQuantifier.Some => any,
            Foundgine.Core.Semantic.Query.SemanticRelationshipQuantifier.None => Expression.Not(any),
            Foundgine.Core.Semantic.Query.SemanticRelationshipQuantifier.All => Expression.Not(
                Expression.Call(AnyMethod, Navigation.Body, Expression.Lambda(Expression.Not(childPredicate.Body), childPredicate.Parameters))),
            _ => throw new NotSupportedException($"Relationship quantifier '{quantifier}' is not supported.")
        };
        return Expression.Lambda(body, parent);
    }

    /// <summary>Builds the correlated COUNT key as a reusable parent selector.</summary>
    public LambdaExpression BuildCountAggregateOrderSelector(LambdaExpression predicate)
    {
        if (predicate.Parameters.Count != 1 || predicate.Parameters[0].Type != typeof(TChild) || predicate.ReturnType != typeof(bool))
            throw new InvalidOperationException($"Relationship '{Name}' COUNT predicate does not target '{typeof(TChild)}'.");
        var count = Expression.Call(OrmEnumerableMethods<TChild, int>.Count, Navigation.Body, predicate);
        return Expression.Lambda<Func<TParent, int>>(count, Navigation.Parameters[0]);
    }

    /// <summary>
    /// Builds one COUNT component comparison for a future lexicographic keyset predicate.
    /// The same authorized child predicate and correlated selector used by ordering are reused.
    /// This primitive is deliberately not wired into FetchAsync until aggregate tuple projection
    /// and provider null/order semantics are implemented together.
    /// </summary>
    public LambdaExpression BuildCountAggregateCursorComparison(
        LambdaExpression predicate,
        int cursorValue,
        bool descending,
        bool equal)
    {
        if (cursorValue < 0)
            throw new ArgumentOutOfRangeException(nameof(cursorValue), "COUNT cursor values cannot be negative.");

        var selector = (Expression<Func<TParent, int>>)BuildCountAggregateOrderSelector(predicate);
        var constant = Expression.Constant(cursorValue, typeof(int));
        Expression comparison = equal
            ? Expression.Equal(selector.Body, constant)
            : descending
                ? Expression.LessThan(selector.Body, constant)
                : Expression.GreaterThan(selector.Body, constant);
        return Expression.Lambda<Func<TParent, bool>>(comparison, selector.Parameters[0]);
    }

    public IOrderedQueryable<TParent> ApplyAggregateOrder(
        IQueryable<TParent> query,
        LambdaExpression predicate,
        Foundgine.Core.Semantic.Query.SemanticOrderAggregate aggregate,
        LambdaExpression? selector,
        bool descending,
        bool thenBy)
    {
        if (predicate.Parameters.Count != 1 || predicate.Parameters[0].Type != typeof(TChild) || predicate.ReturnType != typeof(bool))
            throw new InvalidOperationException($"Relationship '{Name}' aggregate order predicate does not target '{typeof(TChild)}'.");

        if (aggregate == Foundgine.Core.Semantic.Query.SemanticOrderAggregate.Count)
        {
            var keySelector = (Expression<Func<TParent, int>>)BuildCountAggregateOrderSelector(predicate);
            return OrmQueryableOrderMethods<TParent, int>.Apply(query, keySelector, descending, thenBy);
        }

        if (aggregate is not (Foundgine.Core.Semantic.Query.SemanticOrderAggregate.Min or Foundgine.Core.Semantic.Query.SemanticOrderAggregate.Max))
            throw new NotSupportedException($"Relationship '{Name}' aggregate ordering '{aggregate}' is not supported.");
        if (selector is null || selector.Parameters.Count != 1 || selector.Parameters[0].Type != typeof(TChild))
            throw new NotSupportedException($"Relationship '{Name}' {aggregate} ordering requires a generated scalar field selector.");

        // Lift selectors to Nullable<T>. SQL MIN/MAX over an empty related set is NULL;
        // this also makes the expression's CLR semantics match the database semantics.
        var numericType = Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType;
        if (numericType == typeof(byte)) return ApplyNumericAggregateOrder<byte>(query, predicate, selector, aggregate, descending, thenBy);
        if (numericType == typeof(short)) return ApplyNumericAggregateOrder<short>(query, predicate, selector, aggregate, descending, thenBy);
        if (numericType == typeof(int)) return ApplyNumericAggregateOrder<int>(query, predicate, selector, aggregate, descending, thenBy);
        if (numericType == typeof(long)) return ApplyNumericAggregateOrder<long>(query, predicate, selector, aggregate, descending, thenBy);
        if (numericType == typeof(float)) return ApplyNumericAggregateOrder<float>(query, predicate, selector, aggregate, descending, thenBy);
        if (numericType == typeof(double)) return ApplyNumericAggregateOrder<double>(query, predicate, selector, aggregate, descending, thenBy);
        if (numericType == typeof(decimal)) return ApplyNumericAggregateOrder<decimal>(query, predicate, selector, aggregate, descending, thenBy);
        throw new NotSupportedException($"Relationship '{Name}' {aggregate} ordering does not support selector type '{selector.ReturnType.Name}'.");
    }

    private IOrderedQueryable<TParent> ApplyNumericAggregateOrder<TNumber>(
        IQueryable<TParent> query,
        LambdaExpression predicate,
        LambdaExpression selector,
        Foundgine.Core.Semantic.Query.SemanticOrderAggregate aggregate,
        bool descending,
        bool thenBy)
        where TNumber : struct
    {
        var nullableType = typeof(TNumber?);
        var body = selector.ReturnType == nullableType ? selector.Body : Expression.Convert(selector.Body, nullableType);
        var liftedSelector = Expression.Lambda<Func<TChild, TNumber?>>(body, (ParameterExpression)selector.Parameters[0]);
        var filtered = Expression.Call(OrmEnumerableMethods<TChild, TNumber?>.Where, Navigation.Body, predicate);
        var method = aggregate == Foundgine.Core.Semantic.Query.SemanticOrderAggregate.Min
            ? OrmEnumerableMethods<TChild, TNumber?>.Min
            : OrmEnumerableMethods<TChild, TNumber?>.Max;
        var value = Expression.Call(method, filtered, liftedSelector);
        var parentSelector = Expression.Lambda<Func<TParent, TNumber?>>(value, Navigation.Parameters[0]);
        return OrmQueryableOrderMethods<TParent, TNumber?>.Apply(query, parentSelector, descending, thenBy);
    }

    public LambdaExpression BuildAggregate(
        LambdaExpression predicate,
        Foundgine.Core.Semantic.Query.SemanticFilterAggregate aggregate,
        LambdaExpression? selector,
        Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator op,
        object? value)
    {
        if (predicate.Parameters.Count != 1 || predicate.Parameters[0].Type != typeof(TChild) || predicate.ReturnType != typeof(bool))
            throw new InvalidOperationException($"Relationship '{Name}' aggregate predicate does not target '{typeof(TChild)}'.");

        var parent = Navigation.Parameters[0];
        var filtered = Expression.Call(OrmEnumerableMethods<TChild, int>.Where, Navigation.Body, predicate);
        Expression aggregateExpression;
        Type resultType;
        if (aggregate == Foundgine.Core.Semantic.Query.SemanticFilterAggregate.Count)
        {
            aggregateExpression = Expression.Call(OrmEnumerableMethods<TChild, int>.Count, Navigation.Body, predicate);
            resultType = typeof(int);
        }
        else
        {
            if (selector is null || selector.Parameters.Count != 1 || selector.Parameters[0].Type != typeof(TChild))
                throw new NotSupportedException($"Relationship '{Name}' {aggregate} aggregate requires a generated scalar field selector.");
            var selectorType = selector.ReturnType;
            if (!IsSupportedAggregateType(selectorType))
                throw new NotSupportedException($"Relationship '{Name}' {aggregate} aggregate does not support selector type '{selectorType.Name}'.");

            // SQL MIN/MAX over an empty correlated set is NULL. Lift non-nullable numeric
            // selectors before building Enumerable.Min/Max so the in-memory provider has
            // the same empty-set semantics and the expression tree exposes a nullable result.
            resultType = NullableAggregateType(selectorType);
            var liftedSelector = selector.ReturnType == resultType
                ? selector
                : Expression.Lambda(Expression.Convert(selector.Body, resultType), selector.Parameters);
            var method = GetAggregateMethod(resultType, aggregate == Foundgine.Core.Semantic.Query.SemanticFilterAggregate.Min);
            aggregateExpression = Expression.Call(method, filtered, liftedSelector);
        }

        var underlyingType = Nullable.GetUnderlyingType(resultType);
        var convertedValue = Convert.ChangeType(value, underlyingType ?? resultType, System.Globalization.CultureInfo.InvariantCulture);
        Expression constant = underlyingType is null
            ? Expression.Constant(convertedValue, resultType)
            : Expression.Convert(Expression.Constant(convertedValue, underlyingType), resultType);
        Expression comparison = op switch
        {
            Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator.Eq => Expression.Equal(aggregateExpression, constant),
            Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator.Neq => Expression.NotEqual(aggregateExpression, constant),
            Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator.Gt => Expression.GreaterThan(aggregateExpression, constant),
            Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator.Gte => Expression.GreaterThanOrEqual(aggregateExpression, constant),
            Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator.Lt => Expression.LessThan(aggregateExpression, constant),
            Foundgine.Core.Semantic.Query.SemanticAggregateFilterOperator.Lte => Expression.LessThanOrEqual(aggregateExpression, constant),
            _ => throw new NotSupportedException($"Aggregate comparison '{op}' is not supported.")
        };
        return Expression.Lambda(comparison, parent);
    }

    // The type switch closes these generic method handles at compile time. There is no
    // GetMethods()/MakeGenericMethod discovery on the execution path, which keeps this
    // expression builder suitable for trimming and Native AOT analysis.
    private static System.Reflection.MethodInfo GetAggregateMethod(Type resultType, bool minimum) => resultType == typeof(byte) ? (minimum ? OrmEnumerableMethods<TChild, byte>.Min : OrmEnumerableMethods<TChild, byte>.Max)
        : resultType == typeof(byte?) ? (minimum ? OrmEnumerableMethods<TChild, byte?>.Min : OrmEnumerableMethods<TChild, byte?>.Max)
        : resultType == typeof(short) ? (minimum ? OrmEnumerableMethods<TChild, short>.Min : OrmEnumerableMethods<TChild, short>.Max)
        : resultType == typeof(short?) ? (minimum ? OrmEnumerableMethods<TChild, short?>.Min : OrmEnumerableMethods<TChild, short?>.Max)
        : resultType == typeof(int) ? (minimum ? OrmEnumerableMethods<TChild, int>.Min : OrmEnumerableMethods<TChild, int>.Max)
        : resultType == typeof(int?) ? (minimum ? OrmEnumerableMethods<TChild, int?>.Min : OrmEnumerableMethods<TChild, int?>.Max)
        : resultType == typeof(long) ? (minimum ? OrmEnumerableMethods<TChild, long>.Min : OrmEnumerableMethods<TChild, long>.Max)
        : resultType == typeof(long?) ? (minimum ? OrmEnumerableMethods<TChild, long?>.Min : OrmEnumerableMethods<TChild, long?>.Max)
        : resultType == typeof(float) ? (minimum ? OrmEnumerableMethods<TChild, float>.Min : OrmEnumerableMethods<TChild, float>.Max)
        : resultType == typeof(float?) ? (minimum ? OrmEnumerableMethods<TChild, float?>.Min : OrmEnumerableMethods<TChild, float?>.Max)
        : resultType == typeof(double) ? (minimum ? OrmEnumerableMethods<TChild, double>.Min : OrmEnumerableMethods<TChild, double>.Max)
        : resultType == typeof(double?) ? (minimum ? OrmEnumerableMethods<TChild, double?>.Min : OrmEnumerableMethods<TChild, double?>.Max)
        : resultType == typeof(decimal) ? (minimum ? OrmEnumerableMethods<TChild, decimal>.Min : OrmEnumerableMethods<TChild, decimal>.Max)
        : resultType == typeof(decimal?) ? (minimum ? OrmEnumerableMethods<TChild, decimal?>.Min : OrmEnumerableMethods<TChild, decimal?>.Max)
        : throw new NotSupportedException($"Aggregate selector type '{resultType.Name}' is not supported.");

    private static Type NullableAggregateType(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is not null) return type;
        if (type == typeof(byte)) return typeof(byte?);
        if (type == typeof(short)) return typeof(short?);
        if (type == typeof(int)) return typeof(int?);
        if (type == typeof(long)) return typeof(long?);
        if (type == typeof(float)) return typeof(float?);
        if (type == typeof(double)) return typeof(double?);
        if (type == typeof(decimal)) return typeof(decimal?);
        throw new NotSupportedException($"Aggregate selector type '{type.Name}' is not supported.");
    }

    private static bool IsSupportedAggregateType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(byte) || t == typeof(short) || t == typeof(int) || t == typeof(long) ||
               t == typeof(float) || t == typeof(double) || t == typeof(decimal);
    }

}


/// <summary>Compile-time selected Enumerable method handles for one source/result type pair.</summary>
internal static class OrmEnumerableMethods<TSource, TResult>
{
    public static readonly System.Reflection.MethodInfo Where =
        ((MethodCallExpression)((Expression<Func<IEnumerable<TSource>, Func<TSource, bool>, IEnumerable<TSource>>>)
            ((source, predicate) => Enumerable.Where<TSource>(source, predicate))).Body).Method;

    public static readonly System.Reflection.MethodInfo Count =
        ((MethodCallExpression)((Expression<Func<IEnumerable<TSource>, Func<TSource, bool>, int>>)
            ((source, predicate) => Enumerable.Count<TSource>(source, predicate))).Body).Method;

    public static readonly System.Reflection.MethodInfo Min =
        ((MethodCallExpression)((Expression<Func<IEnumerable<TSource>, Func<TSource, TResult>, TResult>>)
            ((source, selector) => Enumerable.Min<TSource, TResult>(source, selector)!)).Body).Method;

    public static readonly System.Reflection.MethodInfo Max =
        ((MethodCallExpression)((Expression<Func<IEnumerable<TSource>, Func<TSource, TResult>, TResult>>)
            ((source, selector) => Enumerable.Max<TSource, TResult>(source, selector)!)).Body).Method;
}

/// <summary>Closed generic Queryable ordering calls; no runtime generic method construction.</summary>
internal static class OrmQueryableOrderMethods<TSource, TKey>
{
    private static readonly System.Reflection.MethodInfo OrderByMethod =
        ((MethodCallExpression)((Expression<Func<IQueryable<TSource>, Expression<Func<TSource, TKey>>, IOrderedQueryable<TSource>>>)
            ((query, selector) => Queryable.OrderBy(query, selector))).Body).Method;
    private static readonly System.Reflection.MethodInfo OrderByDescendingMethod =
        ((MethodCallExpression)((Expression<Func<IQueryable<TSource>, Expression<Func<TSource, TKey>>, IOrderedQueryable<TSource>>>)
            ((query, selector) => Queryable.OrderByDescending(query, selector))).Body).Method;
    private static readonly System.Reflection.MethodInfo ThenByMethod =
        ((MethodCallExpression)((Expression<Func<IOrderedQueryable<TSource>, Expression<Func<TSource, TKey>>, IOrderedQueryable<TSource>>>)
            ((query, selector) => Queryable.ThenBy(query, selector))).Body).Method;
    private static readonly System.Reflection.MethodInfo ThenByDescendingMethod =
        ((MethodCallExpression)((Expression<Func<IOrderedQueryable<TSource>, Expression<Func<TSource, TKey>>, IOrderedQueryable<TSource>>>)
            ((query, selector) => Queryable.ThenByDescending(query, selector))).Body).Method;

    public static IOrderedQueryable<TSource> Apply(IQueryable<TSource> query, Expression<Func<TSource, TKey>> selector, bool descending, bool thenBy)
    {
        var method = thenBy
            ? (descending ? ThenByDescendingMethod : ThenByMethod)
            : (descending ? OrderByDescendingMethod : OrderByMethod);
        var call = Expression.Call(method, query.Expression, Expression.Quote(selector));
        return (IOrderedQueryable<TSource>)query.Provider.CreateQuery<TSource>(call);
    }
}
