using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// A logical, caller-facing traversal that may span multiple physical/semantic
/// relationships. It is an open-intent alias, not a provider join definition.
/// The path is expanded into ordinary semantic relationships before
/// authorization and planning, so every hop retains its own semantics.
/// </summary>
public sealed record SemanticTraversal(
    EntityId Source,
    string Name,
    EntityId Target,
    IReadOnlyList<RelationshipId> Path)
{
    public RelationshipId FirstRelationship => Path[0];
}
