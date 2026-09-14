using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Static description of a domain entity. Metadata describes what exists;
/// it does not describe a request and does not know about GraphQL or SQL.
/// </summary>
public sealed record EntityMetadata(
    EntityId EntityId,
    string Name,
    IReadOnlyList<ColumnMetadata> Columns,
    string? StorageName = null,
    IReadOnlyList<FieldMetadata>? Fields = null,
    ColumnReference? PrimaryKey = null,
    Type? ClrType = null,
    // True when this entity represents an occurrence at a point in time (an
    // event) rather than the current state of something. See
    // FoundgineEventAttribute. Defaults to false (a state entity).
    bool IsEvent = false,
    // The column carrying the timestamp the event occurred at, when declared
    // and the entity IsEvent. Null for state entities and for events that did
    // not declare a temporal column.
    ColumnReference? TemporalColumn = null,
    IReadOnlyList<AliasDeclaration>? Aliases = null)
{
    public string EffectiveStorageName => StorageName ?? Name;
    public IReadOnlyList<FieldMetadata> EffectiveFields => Fields ?? [];
}