namespace Foundgine.Core.Semantic;

/// <summary>
/// Provider-independent semantic type information. CLR types may still be
/// retained by adapters/providers, but semantic consumers should use this
/// contract rather than depending on System.Type.
/// </summary>
public abstract record SemanticType
{
    public sealed record Scalar(SemanticScalarKind Kind) : SemanticType;

    public sealed record Enum(string Name) : SemanticType;

    public sealed record Object(string Name) : SemanticType;

    public sealed record Collection(SemanticType ElementType) : SemanticType;

    public static SemanticType FromClrType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return FromClrType(nullable);

        if (type == typeof(byte[])) return new Scalar(SemanticScalarKind.Bytes);
        if (type.IsArray)
            return new Collection(FromClrType(type.GetElementType()!));

        if (type.IsEnum)
            return new Enum(type.Name);

        if (type == typeof(string) || type == typeof(char)) return new Scalar(SemanticScalarKind.String);
        if (type == typeof(bool)) return new Scalar(SemanticScalarKind.Boolean);
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int)) return new Scalar(SemanticScalarKind.Int32);
        if (type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return new Scalar(SemanticScalarKind.Int64);
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new Scalar(SemanticScalarKind.Decimal);
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return new Scalar(SemanticScalarKind.DateTime);
        if (type == typeof(Guid)) return new Scalar(SemanticScalarKind.Guid);

        var enumerable = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null)
            return new Collection(FromClrType(enumerable.GetGenericArguments()[0]));

        return new Object(type.Name);
    }
}

public enum SemanticScalarKind : byte
{
    String,
    Int32,
    Int64,
    Decimal,
    Boolean,
    DateTime,
    Guid,
    Bytes
}