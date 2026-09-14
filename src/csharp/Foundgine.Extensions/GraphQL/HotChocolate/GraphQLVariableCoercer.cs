using System.Collections;
using System.Globalization;
using System.Text.Json;
using HotChocolate.Language;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Performs GraphQL variable input coercion at the adapter boundary.
/// Foundgine core receives ordinary CLR values and never sees GraphQL
/// variable/type syntax.
/// </summary>
public static class GraphQLVariableCoercer
{
    public static object? Resolve(
        string name,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> definitions)
    {
        if (!definitions.TryGetValue(name, out var definition))
            throw new InvalidOperationException($"GraphQL variable '${name}' is not defined by the operation.");

        object? runtimeValue = null;
        var supplied = variables is not null && variables.TryGetValue(name, out runtimeValue);
        var value = supplied
            ? Normalize(runtimeValue)
            : definition.DefaultValue is not null
                ? FromSyntax(definition.DefaultValue)
                : null;

        if (!supplied && definition.DefaultValue is null && IsNonNull(definition.Type))
            throw new InvalidOperationException(
                $"GraphQL variable '${name}' requires a runtime variable-value dictionary; it was not supplied, and its non-null type requires a value.");

        return Coerce(value, definition.Type, $"${name}");
    }

    public static object? ResolveValue(
        IValueNode node,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> definitions) =>
        ResolveSyntax(node, variables, definitions);

    public static void ValidateSuppliedVariables(
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> definitions)
    {
        // GraphQL variable values may contain keys that are not declared by the
        // selected operation. Those extra runtime values are ignored. Validation
        // is performed when a declared variable is resolved/coerced.
    }

    private static object? Coerce(object? value, ITypeNode type, string path)
    {
        if (type is NonNullTypeNode nonNull)
        {
            if (value is null)
                throw new InvalidOperationException(
                    $"GraphQL variable value at {path} cannot be null because its type is non-null.");
            return Coerce(value, nonNull.Type, path);
        }

        if (value is null)
            return null;

        if (type is ListTypeNode listType)
        {
            if (value is IDictionary || value is IReadOnlyDictionary<string, object?>)
                throw new InvalidOperationException(
                    $"GraphQL variable value at {path} must be a list.");

            if (value is string || value is not IEnumerable enumerable)
                return new[] { Coerce(value, listType.Type, $"{path}[0]") };

            return enumerable.Cast<object?>()
                .Select((item, index) => Coerce(item, listType.Type, $"{path}[{index}]"))
                .ToArray();
        }

        var named = type.ToString();
        return CoerceNamed(value, named, path);
    }

    private static object? CoerceNamed(object value, string typeName, string path)
    {
        switch (typeName)
        {
            case "Int":
                return CoerceInt(value, path);
            case "Float":
                return CoerceFloat(value, path);
            case "String":
                return value is string
                    ? value
                    : throw TypeError(path, "String", value);
            case "Boolean":
                return value is bool
                    ? value
                    : throw TypeError(path, "Boolean", value);
            case "ID":
                return value switch
                {
                    string s => s,
                    sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value,
                        CultureInfo.InvariantCulture)!,
                    _ => throw TypeError(path, "ID", value)
                };
            default:
                // Without the application schema the adapter cannot distinguish
                // an input object from an enum/custom scalar by name. It can,
                // however, enforce the GraphQL input-object shape when the runtime
                // value is object-like and preserve custom scalar values unchanged.
                if (value is IDictionary<string, object?> dictionary)
                    return dictionary.ToDictionary(x => x.Key, x => Normalize(x.Value),
                        StringComparer.OrdinalIgnoreCase);
                if (value is IReadOnlyDictionary<string, object?> readOnly)
                    return readOnly.ToDictionary(x => x.Key, x => Normalize(x.Value), StringComparer.OrdinalIgnoreCase);
                return value;
        }
    }

    private static int CoerceInt(object value, string path) => value switch
    {
        sbyte v => v,
        byte v => v,
        short v => v,
        ushort v => v,
        int v => v,
        uint v when v <= int.MaxValue => (int)v,
        long v when v is >= int.MinValue and <= int.MaxValue => (int)v,
        ulong v when v <= int.MaxValue => (int)v,
        JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var v) => v,
        _ => throw TypeError(path, "Int", value)
    };

    private static double CoerceFloat(object value, string path) => value switch
    {
        sbyte v => v,
        byte v => v,
        short v => v,
        ushort v => v,
        int v => v,
        uint v => v,
        long v => v,
        ulong v => v,
        float v => v,
        double v => v,
        decimal v => (double)v,
        JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var v) => v,
        _ => throw TypeError(path, "Float", value)
    };

    private static object? ResolveSyntax(
        IValueNode node,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> definitions) => node switch
    {
        VariableNode variable => Resolve(variable.Name.Value, variables, definitions),
        StringValueNode s => s.Value,
        IntValueNode i when long.TryParse(i.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) =>
            value,
        FloatValueNode f when double.TryParse(f.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            => value,
        BooleanValueNode b => b.Value,
        NullValueNode => null,
        EnumValueNode e => e.Value,
        ListValueNode list => list.Items.Select(x => ResolveSyntax(x, variables, definitions)).ToArray(),
        ObjectValueNode obj => obj.Fields.ToDictionary(
            x => x.Name.Value,
            x => ResolveSyntax(x.Value, variables, definitions),
            StringComparer.OrdinalIgnoreCase),
        _ => node.ToString()
    };

    private static object? FromSyntax(IValueNode node) => ResolveSyntax(
        node,
        null,
        new Dictionary<string, VariableDefinitionNode>(StringComparer.Ordinal));

    private static object? Normalize(object? value)
    {
        if (value is null)
            return null;
        if (value is JsonElement json)
            return json.ValueKind switch
            {
                JsonValueKind.Object => json.EnumerateObject().ToDictionary(x => x.Name, x => Normalize(x.Value),
                    StringComparer.OrdinalIgnoreCase),
                JsonValueKind.Array => json.EnumerateArray().Select(x => Normalize(x)).ToArray(),
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number => json.TryGetInt64(out var integer) ? integer : json.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => json.ToString()
            };
        if (value is IReadOnlyDictionary<string, object?> readOnly)
            return readOnly.ToDictionary(x => x.Key, x => Normalize(x.Value), StringComparer.OrdinalIgnoreCase);
        if (value is IDictionary<string, object?> dictionary)
            return dictionary.ToDictionary(x => x.Key, x => Normalize(x.Value), StringComparer.OrdinalIgnoreCase);
        if (value is IEnumerable enumerable && value is not string)
            return enumerable.Cast<object?>().Select(Normalize).ToArray();
        return value;
    }

    private static bool IsNonNull(ITypeNode type) => type is NonNullTypeNode;

    private static InvalidOperationException TypeError(string path, string expected, object value) =>
        new($"GraphQL variable value at {path} must be {expected}; received {value.GetType().Name}.");
}