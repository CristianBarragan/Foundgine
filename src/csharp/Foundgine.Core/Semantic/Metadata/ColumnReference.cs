using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// A provider-neutral reference to a physical column. Kept in metadata so
/// later providers can translate storage bindings without putting storage
/// knowledge into the semantic graph.
/// </summary>
public sealed record ColumnReference(EntityId EntityId, ColumnId ColumnId);