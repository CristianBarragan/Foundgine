namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// A compile-time value conversion that may be used while resolving a
/// connection. The converter is ordinary application code; Foundgine only
/// records its identity for AOT planning.
/// </summary>
/// <remarks>
/// Attribute-declared conversions name a static method and leave <see cref="Convert"/> null.
/// Conversions declared through the fluent API carry the delegate itself, because a lambda
/// has no stable method name.
/// </remarks>
public sealed record ConversionMetadata(
    Type SourceType,
    Type TargetType,
    string Method)
{
    /// <summary>Optional converter supplied by the fluent API; null for attribute-declared conversions.</summary>
    public Func<object?, object?>? Convert { get; init; }

    /// <summary>Creates fluent conversion metadata from a strongly typed delegate.</summary>
    public static ConversionMetadata FromDelegate<TSource, TTarget>(Func<TSource, TTarget> convert)
    {
        ArgumentNullException.ThrowIfNull(convert);

        return new ConversionMetadata(
            typeof(TSource),
            typeof(TTarget),
            $"fluent:{typeof(TSource).Name}->{typeof(TTarget).Name}")
        {
            Convert = value => convert((TSource)value!)
        };
    }
}
