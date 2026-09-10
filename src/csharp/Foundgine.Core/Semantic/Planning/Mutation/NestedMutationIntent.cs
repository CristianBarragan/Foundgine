using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// A mutation tree. The root mutation is followed by child mutations attached
/// through metadata relationships. The planner flattens the tree into the
/// existing dependency-aware mutation batch; SQL remains unaware of the tree.
/// </summary>
public sealed record NestedMutationIntent(
    IMutationIntent Mutation,
    IReadOnlyList<NestedMutationChild> Children);

public sealed record NestedMutationChild(
    RelationshipId Relationship,
    NestedMutationIntent Mutation);