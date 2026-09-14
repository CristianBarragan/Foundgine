namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Static declaration of a historical/synonymous semantic name, as discovered
/// from metadata (attributes or hand-authored catalogs). Mirrors
/// <see cref="Foundgine.Core.Semantic.SemanticAlias"/> but lives in the
/// metadata layer so <c>Foundgine.Core.Semantic</c> carries no dependency on
/// the concrete metadata assembly.
/// </summary>
public sealed record AliasDeclaration
{
    /// <summary>The alias name.</summary>
    public string Name { get; }

    /// <summary>
    /// Optional evidence weight in the inclusive range 1-100. Null
    /// when the declaration carries no weight.
    /// </summary>
    public int? Weight { get; }

    public AliasDeclaration(string name, int? weight = null)
    {
        if (weight is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                weight,
                "Alias weight must be between 1 and 100 (inclusive) when specified.");
        }

        Name = name;
        Weight = weight;
    }

    public override string ToString() => Name;
}
