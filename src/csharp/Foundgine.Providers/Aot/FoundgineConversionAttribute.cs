namespace Foundgine.Providers.Aot;

/// <summary>
/// Marks an ordinary static conversion method as available to Foundgine's
/// compile-time connection analysis. The method itself remains application
/// code and is never invoked by the metadata layer.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class FoundgineConversionAttribute : Attribute
{
    public FoundgineConversionAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    public Type SourceType { get; }
    public Type TargetType { get; }
}
