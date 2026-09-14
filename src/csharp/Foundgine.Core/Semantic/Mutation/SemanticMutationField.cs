using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// A semantic field assignment. The field is identified semantically; physical
/// column mapping is deliberately outside this representation.
/// </summary>
public sealed record SemanticMutationField(
    FieldId Field,
    object? Value,
    SemanticMutationValueReference? Source = null);