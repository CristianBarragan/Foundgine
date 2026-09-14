namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Provider-neutral traversal strategy hint. It is an optimization property,
/// not part of semantic meaning; providers may use it when their execution
/// model can safely exploit the hint.
/// </summary>
public enum RelationshipTraversalMode : byte
{
    Default = 0,
    SingleHop = 1,
    SetBased = 2
}
