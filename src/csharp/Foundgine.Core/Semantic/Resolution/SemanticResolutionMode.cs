namespace Foundgine.Core.Semantic.Resolution;

/// <summary>Controls the strategy used when resolving a semantic reference.</summary>
public enum SemanticResolutionMode : byte
{
    Identity,
    SemanticIdentity,
    Traversal,
    CompositeKey,
    TemporalIdentity,
    FuzzyMatch
}