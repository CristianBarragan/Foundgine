namespace Foundgine.Core.Semantic;

/// <summary>
/// A historical semantic name that resolves to the owning canonical declaration.
/// Aliases never change the declaration's stable identity.
/// </summary>
public sealed record SemanticAlias
{
    /// <summary>The alias name.</summary>
    public string Name { get; }

    /// <summary>
    /// Optional evidence weight in the inclusive range 1-100. When
    /// null, the alias carries no weight and is not considered by weight-based
    /// evidence gating (see <see cref="AliasWeightEvidenceGate"/>).
    /// </summary>
    public int? Weight { get; }

    public SemanticAlias(string name, int? weight = null)
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
