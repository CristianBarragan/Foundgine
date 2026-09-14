using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// References a semantic value produced by an earlier mutation operation.
/// This describes dependency intent, not provider execution mechanics.
/// </summary>
public sealed record SemanticMutationValueReference(
    int SourceOperationIndex,
    FieldId SourceField);