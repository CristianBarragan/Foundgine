using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Static semantic model metadata. A model is a description of application
/// data; it is not an entity and Foundgine never creates or populates it.
/// </summary>
public sealed record ModelMetadata
{
    public ModelId Id { get; }
    public string Name { get; }
    public EntityId? Entity { get; }

    /// <summary>
    /// Optional minimum alias-weight requirement (1-100 inclusive) for this
    /// model's mapped entities. See FoundgineModelAttribute.MinimumWeight and
    /// Foundgine.Core.Semantic.AliasWeightEvidenceGate. Null declares no minimum.
    /// </summary>
    public int? MinimumWeight { get; }

    public ModelMetadata(ModelId Id, string Name, EntityId? Entity = null, int? MinimumWeight = null)
    {
        if (MinimumWeight is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumWeight),
                MinimumWeight,
                "MinimumWeight must be between 1 and 100 (inclusive) when specified.");
        }

        this.Id = Id;
        this.Name = Name;
        this.Entity = Entity;
        this.MinimumWeight = MinimumWeight;
    }
}
