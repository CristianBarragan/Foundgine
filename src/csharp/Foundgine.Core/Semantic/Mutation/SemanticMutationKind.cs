namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// Semantic mutation operation kinds. These describe domain intent and are
/// deliberately independent of storage verbs such as INSERT or SQL statements.
/// </summary>
public enum SemanticMutationKind : byte
{
    Create,
    Update,
    Delete,
    Upsert
}
