namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Compile-time correspondence between a semantic source member and a target
/// entity member. This is a description only; Foundgine never creates or
/// populates either object.
/// </summary>
public sealed record ConnectionFieldMetadata(
    string SourceMember,
    string TargetMember,
    Type SourceType,
    Type TargetType,
    string? Converter = null);
