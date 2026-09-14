using System.Linq.Expressions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Runtime;

/// <summary>Open, fluent query authoring surface. Typed and dynamic queries compile to the same ReadIntent.</summary>
public static class FoundgineQueryExtensions
{
    public static TypedQuery<T> Query<T>(this IFoundgine foundgine) => new(foundgine);
    public static DynamicQuery Query(this IFoundgine foundgine, string entity) => new(foundgine, entity);
}

public sealed class TypedQuery<T>
{
    private readonly IFoundgine _foundgine;
    private readonly List<ReadSelection> _selections = [];
    private ReadFilter? _filter;
    private readonly List<ReadOrder> _order = [];
    private int? _limit;
    private int? _offset;
    private string? _after;

    internal TypedQuery(IFoundgine foundgine) =>
        _foundgine = foundgine ?? throw new ArgumentNullException(nameof(foundgine));

    public TypedQuery<T> Select<TProjection>(Expression<Func<T, TProjection>> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        foreach (var name in ExpressionMembers.GetMembers(projection.Body))
            _selections.Add(new ReadSelection(Field: name));
        return this;
    }

    public TypedQuery<T> Include<TChild>(Expression<Func<T, IEnumerable<TChild>>> relationship,
        Action<TypedQuery<TChild>> configure)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(configure);
        var name = ExpressionMembers.GetSingleMember(relationship.Body);
        var child = new TypedQuery<TChild>(_foundgine);
        configure(child);
        _selections.Add(new ReadSelection(Relationship: name, Children: child._selections.ToArray()));
        return this;
    }

    public TypedQuery<T> Include<TChild>(Expression<Func<T, TChild>> relationship, Action<TypedQuery<TChild>> configure)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(configure);
        var name = ExpressionMembers.GetSingleMember(relationship.Body);
        var child = new TypedQuery<TChild>(_foundgine);
        configure(child);
        _selections.Add(new ReadSelection(Relationship: name, Children: child._selections.ToArray()));
        return this;
    }

    public TypedQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _filter = ExpressionFilterCompiler.Compile(predicate.Body);
        return this;
    }

    public TypedQuery<T> OrderBy<TProperty>(Expression<Func<T, TProperty>> field, bool descending = false)
    {
        _order.Add(new ReadOrder(ExpressionMembers.GetSingleMember(field.Body),
            descending ? SemanticSortDirection.Desc : SemanticSortDirection.Asc));
        return this;
    }

    public TypedQuery<T> Take(int limit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        return this;
    }

    public TypedQuery<T> Skip(int offset)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        _offset = offset;
        return this;
    }

    public TypedQuery<T> After(string cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
        _after = cursor;
        return this;
    }

    public TypedQuery<T> WithSecurity(SecurityExecutionContext security)
    {
        ArgumentNullException.ThrowIfNull(security);
        _security = security;
        return this;
    }

    public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _foundgine.ExecuteAsync(ToIntent(), cancellationToken: cancellationToken);

    /// <summary>Returns the provider-neutral open intent without executing it.</summary>
    public ReadIntent ToIntent() => new(typeof(T).Name, _selections.ToArray(), _filter, _order.ToArray(), _limit,
        _offset, _after, _security);

    private SecurityExecutionContext? _security;
}

public sealed class DynamicQuery
{
    private readonly IFoundgine _foundgine;
    private readonly string _entity;
    private readonly List<ReadSelection> _selections = [];
    private ReadFilter? _filter;
    private readonly List<ReadOrder> _order = [];
    private int? _limit;
    private int? _offset;
    private string? _after;

    internal DynamicQuery(IFoundgine foundgine, string entity)
    {
        _foundgine = foundgine ?? throw new ArgumentNullException(nameof(foundgine));
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        _entity = entity;
    }

    public DynamicQuery Select(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var field in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            _selections.Add(new ReadSelection(Field: field));
        }

        return this;
    }

    public DynamicQuery Include(string relationship, Action<DynamicQuery> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationship);
        ArgumentNullException.ThrowIfNull(configure);
        var child = new DynamicQuery(_foundgine, relationship);
        configure(child);
        _selections.Add(new ReadSelection(Relationship: relationship, Children: child._selections.ToArray()));
        return this;
    }

    public DynamicQuery Where(string field, SemanticFilterOperator op, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _filter = new ReadFieldFilter(field, op, value);
        return this;
    }

    public DynamicQuery WhereRelated(
        string relationship,
        SemanticRelationshipQuantifier quantifier,
        Action<DynamicQuery> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationship);
        ArgumentNullException.ThrowIfNull(configure);
        var child = new DynamicQuery(_foundgine, relationship);
        configure(child);
        if (child._filter is null)
            throw new ArgumentException("A related filter requires a child Where expression.", nameof(configure));
        _filter = new ReadRelationshipFilter(relationship, quantifier, child._filter);
        return this;
    }

    public DynamicQuery AndWhere(string field, SemanticFilterOperator op, object? value)
    {
        var next = new ReadFieldFilter(field, op, value);
        _filter = _filter is null ? next : new ReadAndFilter([_filter, next]);
        return this;
    }

    public DynamicQuery OrWhere(string field, SemanticFilterOperator op, object? value)
    {
        var next = new ReadFieldFilter(field, op, value);
        _filter = _filter is null ? next : new ReadOrFilter([_filter, next]);
        return this;
    }

    public DynamicQuery OrderBy(string field, bool descending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _order.Add(new ReadOrder(field, descending ? SemanticSortDirection.Desc : SemanticSortDirection.Asc));
        return this;
    }

    public DynamicQuery OrderByPath(
        string field,
        bool descending = false,
        params string[] relationshipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(relationshipPath);
        _order.Add(new ReadOrder(
            field,
            descending ? SemanticSortDirection.Desc : SemanticSortDirection.Asc,
            relationshipPath));
        return this;
    }

    public DynamicQuery Take(int limit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        return this;
    }

    public DynamicQuery Skip(int offset)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        _offset = offset;
        return this;
    }

    public DynamicQuery After(string cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
        _after = cursor;
        return this;
    }

    public DynamicQuery WithSecurity(SecurityExecutionContext security)
    {
        ArgumentNullException.ThrowIfNull(security);
        _security = security;
        return this;
    }

    public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _foundgine.ExecuteAsync(ToIntent(), cancellationToken: cancellationToken);

    /// <summary>Returns the provider-neutral open intent without executing it.</summary>
    public ReadIntent ToIntent() => new(_entity, _selections.ToArray(), _filter, _order.ToArray(), _limit, _offset,
        _after, _security);

    private SecurityExecutionContext? _security;
}

internal static class ExpressionMembers
{
    public static string GetSingleMember(Expression expression)
    {
        expression = Unwrap(expression);
        if (expression is MemberExpression { Member: System.Reflection.PropertyInfo property })
            return property.Name;
        throw new ArgumentException("The query expression must reference a property directly, such as x => x.Name.");
    }

    public static IReadOnlyList<string> GetMembers(Expression expression)
    {
        expression = Unwrap(expression);
        if (expression is NewExpression n)
            return n.Arguments.Select(GetSingleMember).ToArray();
        return [GetSingleMember(expression)];
    }

    private static Expression Unwrap(Expression expression) => expression is UnaryExpression
    {
        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
    } u
        ? u.Operand
        : expression;
}

internal static class ExpressionFilterCompiler
{
    public static ReadFilter Compile(Expression expression)
    {
        expression = Unwrap(expression);
        if (expression is BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.And } and)
            return new ReadAndFilter([Compile(and.Left), Compile(and.Right)]);
        if (expression is BinaryExpression { NodeType: ExpressionType.OrElse or ExpressionType.Or } or)
            return new ReadOrFilter([Compile(or.Left), Compile(or.Right)]);
        if (expression is BinaryExpression binary)
        {
            var op = binary.NodeType switch
            {
                ExpressionType.Equal => SemanticFilterOperator.Eq,
                ExpressionType.NotEqual => SemanticFilterOperator.Neq,
                _ => throw new ArgumentException(
                    "Typed Where currently supports ==, !=, && and ||. Other operators belong in the semantic predicate algebra.")
            };
            var field = binary.Left;
            var value = Evaluate(binary.Right);
            return new ReadFieldFilter(ExpressionMembers.GetSingleMember(field), op, value);
        }

        throw new ArgumentException("Typed Where must be a property comparison, such as x => x.TenantId == tenantId.");
    }

    private static object? Evaluate(Expression expression)
    {
        expression = Unwrap(expression);
        if (expression is ConstantExpression constant) return constant.Value;
        if (expression is MemberExpression { Expression: ConstantExpression })
            return Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object))).Compile()();
        throw new ArgumentException("Typed filter values must be constants or captured local values.");
    }

    private static Expression Unwrap(Expression expression) => expression is UnaryExpression
    {
        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
    } u
        ? u.Operand
        : expression;
}