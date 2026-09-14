using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Providers.Storage.Sql.Query;

/// <summary>
/// Lowers the small AOT authorization predicate IR into provider-neutral SQL
/// fragments. Context values remain parameterized and are resolved only at
/// execution time.
/// </summary>
internal static class SqlAuthorizationWriter
{
    public static string Write(
        AuthorizationPredicate predicate,
        EntityMetadata resource,
        string resourceAlias,
        ICollection<SqlParameterBinding> parameters)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(parameters);

        var counter = 0;
        return WriteNode(predicate, resource, resourceAlias, parameters, ref counter);
    }

    private static string WriteNode(
        AuthorizationPredicate node,
        EntityMetadata resource,
        string resourceAlias,
        ICollection<SqlParameterBinding> parameters,
        ref int counter) => node.Kind switch
    {
        AuthorizationPredicateKind.ContextParameter =>
            throw new InvalidOperationException("A context parameter must be followed by member access."),
        AuthorizationPredicateKind.ResourceParameter =>
            resourceAlias,
        AuthorizationPredicateKind.Parameter =>
            throw new NotSupportedException("Untyped authorization parameters are not supported by the SQL provider."),
        AuthorizationPredicateKind.MemberAccess => WriteMember(node, resource, resourceAlias, parameters, ref counter),
        AuthorizationPredicateKind.Constant => AddConstant(node.Value, parameters, ref counter),
        AuthorizationPredicateKind.Equal => Binary(node, "=", resource, resourceAlias, parameters, ref counter),
        AuthorizationPredicateKind.NotEqual => Binary(node, "<>", resource, resourceAlias, parameters, ref counter),
        AuthorizationPredicateKind.And => Binary(node, "AND", resource, resourceAlias, parameters, ref counter),
        AuthorizationPredicateKind.Or => Binary(node, "OR", resource, resourceAlias, parameters, ref counter),
        AuthorizationPredicateKind.Not =>
            $"NOT ({WriteRequired(node.Left, resource, resourceAlias, parameters, ref counter)})",
        _ => throw new NotSupportedException(
            $"Authorization predicate '{node.Kind}' is not supported by the SQL provider.")
    };

    private static string WriteMember(
        AuthorizationPredicate node,
        EntityMetadata resource,
        string resourceAlias,
        ICollection<SqlParameterBinding> parameters,
        ref int counter)
    {
        var target = node.Left ?? throw new InvalidOperationException("Member access has no target.");
        if (target.Kind == AuthorizationPredicateKind.ResourceParameter)
        {
            var name = node.Name ?? throw new InvalidOperationException("Resource member has no name.");
            var field = resource.EffectiveFields.FirstOrDefault(x => x.Name == name);
            if (field?.Column is null)
                throw new InvalidOperationException(
                    $"Authorization resource member '{resource.Name}.{name}' has no storage column mapping.");

            var column = resource.Columns.FirstOrDefault(x => x.Id == field.Column.ColumnId)
                         ?? throw new InvalidOperationException(
                             $"Authorization resource member '{resource.Name}.{name}' references a missing column.");
            return
                $"{SqlCompiler.QuoteIdentifier(resourceAlias)}.{SqlCompiler.QuoteIdentifier(column.EffectiveStorageName)}";
        }

        if (target.Kind == AuthorizationPredicateKind.ContextParameter)
        {
            var contextName = target.Name ?? throw new InvalidOperationException("Context parameter has no name.");
            var member = node.Name ?? throw new InvalidOperationException("Context member has no name.");
            var path = contextName + "." + member;
            var name = "auth" + counter++;
            parameters.Add(new SqlParameterBinding(name, null, ContextPath: path));
            return "@" + name;
        }

        throw new NotSupportedException(
            "Only direct resource and context member access is supported by the SQL authorization provider.");
    }

    private static string Binary(
        AuthorizationPredicate node,
        string op,
        EntityMetadata resource,
        string resourceAlias,
        ICollection<SqlParameterBinding> parameters,
        ref int counter)
    {
        var left = WriteRequired(node.Left, resource, resourceAlias, parameters, ref counter);
        var right = WriteRequired(node.Right, resource, resourceAlias, parameters, ref counter);
        return $"({left} {op} {right})";
    }

    private static string AddConstant(string? value, ICollection<SqlParameterBinding> parameters, ref int counter)
    {
        var name = "auth" + counter++;
        parameters.Add(new SqlParameterBinding(name, ParseConstant(value)));
        return "@" + name;
    }

    private static object? ParseConstant(string? value) => value switch
    {
        null => null,
        "null" => null,
        "true" => true,
        "false" => false,
        _ when int.TryParse(value, out var i) => i,
        _ when long.TryParse(value, out var l) => l,
        _ when decimal.TryParse(value, out var d) => d,
        _ => value
    };

    private static string WriteRequired(
        AuthorizationPredicate? node,
        EntityMetadata resource,
        string resourceAlias,
        ICollection<SqlParameterBinding> parameters,
        ref int counter) =>
        node is null
            ? throw new InvalidOperationException("Authorization predicate node is incomplete.")
            : WriteNode(node, resource, resourceAlias, parameters, ref counter);
}