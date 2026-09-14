using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Protocol-neutral description of what a caller wants. Adapters such as
/// GraphQL translate into this shape; the engine never sees their ASTs.
/// </summary>
public sealed record SemanticRequest(
    EntityId Root,
    IReadOnlyList<SemanticSelection> Selections,
    SemanticQueryOptions? Options = null,
    SecurityExecutionContext? Security = null);

public sealed record SemanticSelection(
    FieldId? Field,
    RelationshipId? Relationship,
    IReadOnlyList<SemanticSelection> Children);
