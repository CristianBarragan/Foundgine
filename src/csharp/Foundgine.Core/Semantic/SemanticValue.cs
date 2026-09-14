namespace Foundgine.Core.Semantic;

/// <summary>
/// Provider-independent, canonical semantic value. Adapters may still accept
/// CLR objects at their boundary, but semantic validation and canonicalization
/// can operate on this representation without depending on a provider type.
/// </summary>
public readonly record struct SemanticValue(SemanticValueKind Kind, object? Value)
{
    public static SemanticValue Null => new(SemanticValueKind.Null, null);

    public static SemanticValue From(object? value)
    {
        if (value is null) return Null;
        return value switch
        {
            string or char => new(SemanticValueKind.String, value.ToString()),
            bool v => new(SemanticValueKind.Boolean, v),
            byte or sbyte or short or ushort or int or uint or long or ulong => new(SemanticValueKind.Int64,
                Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
            float or double or decimal => new(SemanticValueKind.Decimal,
                Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture)),
            DateTime v => new(SemanticValueKind.DateTime, v.ToUniversalTime()),
            DateTimeOffset v => new(SemanticValueKind.DateTime, v.ToUniversalTime()),
            Guid v => new(SemanticValueKind.Guid, v),
            Enum v => new(SemanticValueKind.Enum, v.ToString()),
            System.Collections.IEnumerable values when value is not string => new(SemanticValueKind.List,
                values.Cast<object?>().Select(From).ToArray()),
            _ => new(SemanticValueKind.Object, value.ToString())
        };
    }

    public override string ToString() => Kind switch
    {
        SemanticValueKind.Null => "null",
        SemanticValueKind.List =>
            $"[{string.Join(',', ((IReadOnlyList<SemanticValue>)Value!).Select(x => x.ToString()))}]",
        _ => Value?.ToString() ?? "null"
    };
}

public enum SemanticValueKind : byte
{
    Null,
    String,
    Boolean,
    Int64,
    Decimal,
    DateTime,
    Guid,
    Enum,
    List,
    Object
}