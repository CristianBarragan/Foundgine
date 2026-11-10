using System.Globalization;

namespace Foundgine.Providers.Orm;

/// <summary>Reflection-light coercion of IR/context values to the CLR type of a generated field.</summary>
public static class OrmValue
{
    public static T Coerce<T>(object? value)
    {
        if (value is null)
        {
            if (default(T) is null) return default!;
            throw new InvalidOperationException(
                $"A null value cannot be bound to non-nullable '{typeof(T).Name}'.");
        }

        if (value is T exact) return exact;

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        object converted;

        if (target.IsEnum)
            converted = value is string s ? Enum.Parse(target, s, ignoreCase: false) : Enum.ToObject(target, value);
        else if (target == typeof(Guid))
            converted = Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        else if (target == typeof(DateTimeOffset))
            converted = value is DateTime dt
                ? new DateTimeOffset(dt)
                : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        else if (target == typeof(DateOnly))
            converted = value is DateTime d ? DateOnly.FromDateTime(d)
                : DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        else if (target == typeof(TimeOnly))
            converted = value is TimeSpan t ? TimeOnly.FromTimeSpan(t)
                : TimeOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        else
            converted = Convert.ChangeType(value, target, CultureInfo.InvariantCulture);

        return (T)converted;
    }

    /// <summary>Parses the string constants carried by AuthorizationPredicate (same rules as the SQL provider).</summary>
    public static object? ParseConstant(string? value) => value switch
    {
        null => null,
        "null" => null,
        "true" => true,
        "false" => false,
        _ when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        _ when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => value
    };
}
