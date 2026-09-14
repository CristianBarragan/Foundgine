using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

public sealed record SemanticTraversalOrigin(
    EntityId SourceEntity,
    IReadOnlyList<RelationshipId> Path);

public sealed record SemanticIntentOrigin(
    string Operation,
    string? Source = null);

public enum SemanticExpectedCardinality : byte
{
    Unknown,
    One,
    Many
}