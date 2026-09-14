using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

public sealed record ColumnMetadata(
    ColumnId Id,
    string Name,
    string? StorageName = null)
{
    public string EffectiveStorageName => StorageName ?? Name;
}