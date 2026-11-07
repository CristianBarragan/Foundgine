using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>A property or field selected by a configuration lambda such as <c>x =&gt; x.Name</c>.</summary>
internal readonly record struct MemberRef(string Name, Type Type);

/// <summary>
/// Reads member selections out of configuration lambdas. Only the member name and
/// declared type are read; the lambda is never compiled or invoked, so this stays
/// trim/AOT friendly and never touches an instance of the application model.
/// </summary>
internal static class ConfigurationExpression
{
    public static MemberRef Member<TSource, TProperty>(Expression<Func<TSource, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return TryMember(selector.Body, selector.Parameters[0])
               ?? throw new ArgumentException(
                   $"Expression '{selector}' must select a single property or field of {typeof(TSource).Name}, for example x => x.Name.",
                   nameof(selector));
    }

    /// <summary>Reads either a single member (<c>x =&gt; x.A</c>) or a composite (<c>x =&gt; new { x.A, x.B }</c>).</summary>
    public static IReadOnlyList<MemberRef> Members<TSource>(Expression<Func<TSource, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var body = Unwrap(selector.Body);
        if (body is NewExpression { Arguments.Count: > 0 } created)
        {
            var members = new List<MemberRef>(created.Arguments.Count);
            foreach (var argument in created.Arguments)
            {
                members.Add(TryMember(argument, selector.Parameters[0])
                            ?? throw new ArgumentException(
                                $"Expression '{selector}' may only contain direct members of {typeof(TSource).Name}.",
                                nameof(selector)));
            }

            return members;
        }

        return [Member(selector)];
    }

    public static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>Element type of a collection navigation, or null when it cannot be determined.</summary>
    public static Type? ElementType(Type collectionType)
    {
        if (collectionType.IsArray)
            return collectionType.GetElementType();

        if (collectionType.IsGenericType)
        {
            var arguments = collectionType.GetGenericArguments();
            if (arguments.Length == 1)
                return arguments[0];
        }

        return null;
    }

    private static MemberRef? TryMember(Expression expression, ParameterExpression parameter)
    {
        if (Unwrap(expression) is MemberExpression { Expression: ParameterExpression owner } member &&
            owner == parameter)
        {
            var type = member.Member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => null
            };

            if (type is not null)
                return new MemberRef(member.Member.Name, type);
        }

        return null;
    }

    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            expression = unary.Operand;

        return expression;
    }
}
