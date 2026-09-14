using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// A semantic dependency between mutation operations.
/// </summary>
public sealed record SemanticMutationDependency(
    int SourceOperationIndex,
    int TargetOperationIndex,
    FieldId SourceField,
    FieldId TargetField,
    RelationshipId? Relationship = null);
