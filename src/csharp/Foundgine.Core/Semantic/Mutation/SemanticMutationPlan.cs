using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// Canonical provider-neutral planning artifact for a semantic mutation graph.
/// Dependencies are the single semantic representation of value flow across
/// operation boundaries. A dependency inherently requires the produced value to
/// retain its logical source identity until the target field consumes it.
/// No physical correlation column, SQL alias, or provider mechanism appears here.
/// </summary>
public sealed record SemanticMutationPlan(
    IReadOnlyList<SemanticMutationOperationPlan> Operations,
    IReadOnlyList<SemanticMutationDependencyPlan> Dependencies)
{
    /// <summary>Security invariants required to execute this exact semantic mutation plan.</summary>
    public IReadOnlyList<string> RequiredSecurityInvariants { get; init; } = [];
}

public sealed record SemanticMutationOperationPlan(
    string OperationId,
    EntityId Entity,
    SemanticMutationKind Kind,
    IReadOnlyList<SemanticMutationField> Fields,
    SemanticFilterExpression? Filter,
    IReadOnlyList<FieldId> ConflictFields,
    IReadOnlyList<FieldId> ReturnFields,
    IReadOnlyList<SemanticMutationEffect> Effects);

public sealed record SemanticMutationDependencyPlan(
    string FromOperationId,
    string ToOperationId,
    FieldId SourceField,
    FieldId TargetField,
    RelationshipId? Relationship = null);