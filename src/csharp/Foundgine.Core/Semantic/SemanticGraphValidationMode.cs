namespace Foundgine.Core.Semantic;

/// <summary>Controls how strictly a semantic graph is validated.</summary>
public enum SemanticGraphValidationMode : byte
{
    /// <summary>Canonical IR: one root, fully declared topology, no unresolved edges.</summary>
    Strict,

    /// <summary>Allows multiple independent roots while retaining declared-edge checks.</summary>
    Loose,

    /// <summary>Allows unresolved relationship targets/edges for federated planning.</summary>
    Federated,

    /// <summary>Allows partial graphs intended for exploratory intent inspection.</summary>
    Exploratory
}