using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Domain-facing field. ClrType is retained for compatibility with existing
/// providers; SemanticType is the provider-neutral contract for semantic code.
/// </summary>
public sealed record SemanticField(
    FieldId Id,
    string Name,
    Type ClrType,
    SemanticType? SemanticType = null,
    SemanticFieldCapabilities Capabilities = SemanticFieldCapabilities.Default,
    IReadOnlyList<SemanticAlias>? Aliases = null,
    IReadOnlyList<SemanticConstraint>? Constraints = null,
    bool? NullableOverride = null)
{
    public IReadOnlyList<SemanticAlias> EffectiveAliases => Aliases ?? [];
    public IReadOnlyList<SemanticConstraint> EffectiveConstraints => Constraints ?? [];

    public SemanticType EffectiveSemanticType =>
        SemanticType ?? Foundgine.Core.Semantic.SemanticType.FromClrType(ClrType);

    /// <summary>
    /// Whether the semantic field permits null. For reference types this may
    /// be explicitly supplied by the typed/AOT semantic pipeline so nullable-
    /// reference metadata is preserved even though string and string? share
    /// the same runtime <see cref="Type"/>.
    /// </summary>
    public bool IsNullable =>
        NullableOverride ?? (!ClrType.IsValueType || Nullable.GetUnderlyingType(ClrType) is not null);
}

[Flags]
public enum SemanticFieldCapabilities : byte
{
    None = 0,
    Filterable = 1 << 0,
    Sortable = 1 << 1,
    Selectable = 1 << 2,
    Aggregatable = 1 << 3,
    Writable = 1 << 4,
    Computed = 1 << 5,
    Sensitive = 1 << 6,
    Deprecated = 1 << 7,
    Default = Filterable | Sortable | Selectable | Aggregatable
}