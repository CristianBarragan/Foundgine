namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// A compile-time value conversion that may be used while resolving a
/// connection. The converter is ordinary application code; Foundgine only
/// records its identity for AOT planning.
/// </summary>
public sealed record ConversionMetadata(
    Type SourceType,
    Type TargetType,
    string Method);