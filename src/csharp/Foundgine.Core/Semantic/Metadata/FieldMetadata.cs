using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Static field metadata, including an optional provider-neutral storage
/// column reference. Providers decide how to translate that reference.
/// </summary>
public sealed record FieldMetadata(
    FieldId Id,
    string Name,
    Type ClrType,
    ColumnReference? Column = null,
    // The semantic dimension this field represents (e.g. "tenant", "country",
    // "category"), or null when the field is a plain data value with no
    // declared semantic role. See FoundgineSemanticDimensionAttribute.
    string? Dimension = null,
    // Hints that this field is backed by (or should be backed by) a storage
    // index, for use by query planners choosing access paths.
    bool IsIndexed = false,
    IReadOnlyList<AliasDeclaration>? Aliases = null);