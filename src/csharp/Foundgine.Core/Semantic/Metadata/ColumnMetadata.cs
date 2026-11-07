using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

public sealed record ColumnMetadata(
    ColumnId Id,
    string Name,
    string? StorageName = null)
{
    public string EffectiveStorageName => StorageName ?? Name;

    /// <summary>Maximum length declared by persistence configuration, or null when unconstrained/unknown.</summary>
    public int? MaxLength { get; init; }

    /// <summary>True when persistence configuration declares the column required (not null).</summary>
    public bool IsRequired { get; init; }
}
