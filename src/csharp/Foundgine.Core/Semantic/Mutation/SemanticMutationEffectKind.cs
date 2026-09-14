namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// Semantic effects produced by a mutation. Effects describe meaning rather
/// than physical database operations.
/// </summary>
public enum SemanticMutationEffectKind : byte
{
    CreateEntity,
    UpdateEntity,
    UpsertEntity,
    DeleteEntity,
    SetField,
    ConnectRelationship,
    DisconnectRelationship
}